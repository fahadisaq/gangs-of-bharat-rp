using System;
using System.Collections.Generic;
using GangsOfBharat.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GangsOfBharat.Client
{
    /// <summary>Encodes 20 ms mono 16 kHz PCM frames to bytes and back.</summary>
    public interface IVoiceCodec
    {
        /// <summary>Samples per frame at 16 kHz (320 = 20 ms).</summary>
        int FrameSamples { get; }
        int Encode(float[] pcm, byte[] output);
        /// <summary>Returns the number of samples written to pcm (FrameSamples).</summary>
        int Decode(byte[] data, int length, float[] pcm);
    }

    /// <summary>
    /// Placeholder codec so the voice path works end to end before Opus is added: 16 kHz is
    /// averaged down to 8 kHz and each sample is stored as 8-bit mu-law, 160 bytes per 20 ms
    /// (64 kbps). That is about 4x the bandwidth of Opus at 16 kbps.
    /// TODO: replace with Opus. Options: Concentus (pure C# Opus port, MIT/BSD, works on
    /// IL2CPP without native code) or a native libopus plugin per platform. Opus frames are
    /// about 40 bytes at 16 kbps and fit ProtocolConstants.MaxVoiceBytes (256).
    /// </summary>
    public sealed class MuLawPlaceholderCodec : IVoiceCodec
    {
        public int FrameSamples { get { return 320; } }

        public int Encode(float[] pcm, byte[] output)
        {
            int n = FrameSamples / 2;
            for (int i = 0; i < n; i++)
            {
                float s = (pcm[2 * i] + pcm[2 * i + 1]) * 0.5f;
                output[i] = LinearToMuLaw((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
            }
            return n;
        }

        public int Decode(byte[] data, int length, float[] pcm)
        {
            int n = Math.Min(length, FrameSamples / 2);
            for (int i = 0; i < n; i++)
            {
                float s = MuLawToLinear(data[i]) / 32768f;
                pcm[2 * i] = s;
                pcm[2 * i + 1] = s;
            }
            for (int i = n * 2; i < FrameSamples; i++)
            {
                pcm[i] = 0f;
            }
            return FrameSamples;
        }

        private static byte LinearToMuLaw(short sample)
        {
            const int Bias = 0x84;
            const int Clip = 32635;
            int sign = (sample >> 8) & 0x80;
            int s = sign != 0 ? -sample : sample;
            if (s > Clip)
            {
                s = Clip;
            }
            s += Bias;
            int exponent = 7;
            for (int mask = 0x4000; (s & mask) == 0 && exponent > 0; mask >>= 1)
            {
                exponent--;
            }
            int mantissa = (s >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        private static short MuLawToLinear(byte value)
        {
            int u = ~value & 0xFF;
            int sign = u & 0x80;
            int exponent = (u >> 4) & 0x07;
            int mantissa = u & 0x0F;
            int s = ((mantissa << 3) + 0x84) << exponent;
            s -= 0x84;
            return (short)(sign != 0 ? -s : s);
        }
    }

    /// <summary>
    /// In-game voice. Capture: Microphone at 16 kHz, cut into 20 ms frames, encoded and sent as
    /// unreliable VoiceFrame messages. Modes: push-to-talk (hold the HUD button or V) or open
    /// mic with a simple level gate. Channels: Proximity (server forwards to players within
    /// 40 m, played as 3D audio at the speaker) or FactionRadio (server forwards to every
    /// online faction member map-wide, played as 2D radio audio). The server only relays
    /// frames; it never decodes or logs voice.
    /// UNVERIFIED: written against Unity 6 APIs, not compiled in the sandbox.
    /// </summary>
    public sealed class VoiceChat : MonoBehaviour
    {
        public enum TalkMode
        {
            PushToTalk,
            OpenMic,
        }

        private const int SampleRate = 16000;
        private const int PlaybackBufferSamples = SampleRate; // 1 s jitter buffer per speaker

        [SerializeField] private TalkMode mode = TalkMode.PushToTalk;
        [SerializeField] private VoiceChannel channel = VoiceChannel.Proximity;
        [SerializeField] private EntityInterpolator entities;
        [Tooltip("Open-mic gate: RMS level above which a frame is sent.")]
        [SerializeField] private float openMicThreshold = 0.02f;
        [SerializeField] private float proximityMaxDistance = 40f;

        private readonly IVoiceCodec _codec = new MuLawPlaceholderCodec();
        private readonly VoiceFrameMessage _out = new VoiceFrameMessage();
        private readonly byte[] _encoded = new byte[ProtocolConstants.MaxVoiceBytes];
        private readonly Dictionary<int, Speaker> _speakers = new Dictionary<int, Speaker>();
        private float[] _frame;
        private float[] _decoded;
        private float[] _readScratch;
        private AudioClip _micClip;
        private string _micDevice;
        private int _micReadPos;
        private bool _pttHeld;
        private ushort _sequence;
        private InputAction _pttKey;
        private InputAction _radioKey;

        public TalkMode Mode { get { return mode; } set { mode = value; } }
        public VoiceChannel Channel { get { return channel; } set { channel = value; } }
        public bool MicAvailable { get { return _micClip != null; } }

        private sealed class Speaker
        {
            public AudioSource Source;
            public readonly float[] Ring = new float[PlaybackBufferSamples];
            public int Read;
            public int Write;
            public int Count;
            public float LastHeard;
            public readonly object Lock = new object();

            public void Push(float[] pcm, int count)
            {
                lock (Lock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (Count == Ring.Length)
                        {
                            Read = (Read + 1) % Ring.Length; // drop oldest on overflow
                            Count--;
                        }
                        Ring[Write] = pcm[i];
                        Write = (Write + 1) % Ring.Length;
                        Count++;
                    }
                }
            }

            /// <summary>Audio thread callback: fills data from the ring buffer, silence when empty.</summary>
            public void Pull(float[] data)
            {
                lock (Lock)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (Count > 0)
                        {
                            data[i] = Ring[Read];
                            Read = (Read + 1) % Ring.Length;
                            Count--;
                        }
                        else
                        {
                            data[i] = 0f;
                        }
                    }
                }
            }
        }

        private void Awake()
        {
            _frame = new float[_codec.FrameSamples];
            _decoded = new float[_codec.FrameSamples];
            _readScratch = new float[_codec.FrameSamples];

            _pttKey = new InputAction("PushToTalk", InputActionType.Button);
            _pttKey.AddBinding("<Keyboard>/v");
            _radioKey = new InputAction("RadioPushToTalk", InputActionType.Button);
            _radioKey.AddBinding("<Keyboard>/b");
        }

        private void OnEnable()
        {
            _pttKey.Enable();
            _radioKey.Enable();
            if (NetClient.Instance != null)
            {
                NetClient.Instance.VoiceReceived += OnVoice;
            }
            StartMicrophone();
        }

        private void OnDisable()
        {
            _pttKey.Disable();
            _radioKey.Disable();
            if (NetClient.Instance != null)
            {
                NetClient.Instance.VoiceReceived -= OnVoice;
            }
            if (_micClip != null)
            {
                Microphone.End(_micDevice);
                _micClip = null;
            }
        }

        private void StartMicrophone()
        {
#if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                return; // retried from the settings screen once the player grants it
            }
#elif UNITY_IOS
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Application.RequestUserAuthorization(UserAuthorization.Microphone);
                return; // iOS also needs NSMicrophoneUsageDescription in Player Settings
            }
#endif
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning(ClientStrings.Get("voice.no_mic"));
                return;
            }
            _micDevice = Microphone.devices[0];
            _micClip = Microphone.Start(_micDevice, true, 1, SampleRate);
            _micReadPos = 0;
        }

        /// <summary>Retry after the permission dialog (call from the settings screen).</summary>
        public void RetryMicrophone()
        {
            if (_micClip == null)
            {
                StartMicrophone();
            }
        }

        /// <summary>Bind to the HUD talk button's PointerDown/PointerUp (EventTrigger).</summary>
        public void SetPushToTalk(bool held)
        {
            _pttHeld = held;
        }

        /// <summary>Bind to the HUD radio button; holding it talks on faction radio.</summary>
        public void SetRadioPushToTalk(bool held)
        {
            channel = held ? VoiceChannel.FactionRadio : VoiceChannel.Proximity;
            _pttHeld = held;
        }

        private void Update()
        {
            CaptureAndSend();
            UpdateSpeakers();
        }

        private void CaptureAndSend()
        {
            if (_micClip == null || NetClient.Instance == null || NetClient.Instance.State != ClientState.Connected)
            {
                return;
            }
            bool radioKey = _radioKey.IsPressed();
            VoiceChannel sendChannel = radioKey ? VoiceChannel.FactionRadio : channel;
            bool talking = mode == TalkMode.OpenMic || _pttHeld || _pttKey.IsPressed() || radioKey;

            int micPos = Microphone.GetPosition(_micDevice);
            int clipSamples = _micClip.samples;
            int available = (micPos - _micReadPos + clipSamples) % clipSamples;
            int frameSamples = _codec.FrameSamples;
            while (available >= frameSamples)
            {
                ReadWrapped(_micReadPos, clipSamples);
                _micReadPos = (_micReadPos + frameSamples) % clipSamples;
                available -= frameSamples;
                if (!talking)
                {
                    continue; // keep draining so we do not send stale audio later
                }
                if (mode == TalkMode.OpenMic && !_pttHeld && !radioKey && Rms(_frame) < openMicThreshold)
                {
                    continue;
                }
                int bytes = _codec.Encode(_frame, _encoded);
                _out.Channel = sendChannel;
                _out.SenderId = 0; // the server fills in the sender
                _out.Sequence = _sequence++;
                _out.SetData(_encoded, 0, bytes);
                NetClient.Instance.SendUnreliable(_out);
            }
        }

        private void ReadWrapped(int start, int clipSamples)
        {
            int n = _frame.Length;
            if (start + n <= clipSamples)
            {
                _micClip.GetData(_frame, start);
                return;
            }
            int first = clipSamples - start;
            float[] head = new float[first];
            _micClip.GetData(head, start);
            Array.Copy(head, 0, _frame, 0, first);
            float[] tail = _readScratch;
            _micClip.GetData(tail, 0);
            Array.Copy(tail, 0, _frame, first, n - first);
        }

        private static float Rms(float[] pcm)
        {
            float sum = 0f;
            for (int i = 0; i < pcm.Length; i++)
            {
                sum += pcm[i] * pcm[i];
            }
            return Mathf.Sqrt(sum / pcm.Length);
        }

        private void OnVoice(VoiceFrameMessage frame)
        {
            // The frame object is reused by the connection: decode it now.
            int samples = _codec.Decode(frame.Data, frame.Length, _decoded);
            Speaker speaker = GetSpeaker(frame.SenderId, frame.Channel);
            speaker.Push(_decoded, samples);
            speaker.LastHeard = Time.time;
        }

        private Speaker GetSpeaker(int senderId, VoiceChannel voiceChannel)
        {
            Speaker speaker;
            if (!_speakers.TryGetValue(senderId, out speaker))
            {
                speaker = new Speaker();
                GameObject go = new GameObject("Voice_" + senderId);
                go.transform.SetParent(transform, false);
                AudioSource source = go.AddComponent<AudioSource>();
                Speaker captured = speaker;
                source.clip = AudioClip.Create("voice_" + senderId, SampleRate, 1, SampleRate, true, captured.Pull);
                source.loop = true;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.maxDistance = proximityMaxDistance;
                source.minDistance = 2f;
                source.Play();
                speaker.Source = source;
                _speakers.Add(senderId, speaker);
            }
            // Proximity is 3D at the speaker's position; radio is 2D (heard anywhere).
            speaker.Source.spatialBlend = voiceChannel == VoiceChannel.Proximity ? 1f : 0f;
            return speaker;
        }

        private void UpdateSpeakers()
        {
            foreach (KeyValuePair<int, Speaker> pair in _speakers)
            {
                Speaker speaker = pair.Value;
                EntityInterpolator.RemoteEntity entity;
                if (speaker.Source.spatialBlend > 0f && entities != null &&
                    entities.TryGet(pair.Key, out entity) && entity.View != null)
                {
                    speaker.Source.transform.position = entity.View.transform.position;
                }
            }
            // TODO: destroy speakers silent for more than 30 s to free AudioSources.
        }
    }
}

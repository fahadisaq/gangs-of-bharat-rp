using System;
using UnityEngine;
#if FIREBASE_SDK
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
#endif

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Login and Firebase start-up. Flow: AgeGate confirmed -> sign in -> get a Firebase ID token
    /// -> NetClient.Connect(token). Platform sign-in:
    ///  - Android: Google sign-in.
    ///  - iOS: Sign in with Apple (required by App Store rules when other social logins exist)
    ///    and Google sign-in.
    ///
    /// TODO (cannot be done in the sandbox: no network, no Unity):
    ///  1. Import the Firebase Unity SDK (FirebaseAuth, FirebaseFirestore, FirebaseAnalytics,
    ///     FirebaseCrashlytics) from https://firebase.google.com/download/unity.
    ///  2. Add google-services.json (Android) and GoogleService-Info.plist (iOS) from the
    ///     Firebase console into Assets/. They are project config, not secrets, but keep them
    ///     out of public forks.
    ///  3. Add a Google Sign-In plugin (for example the Google Sign-In Unity plugin or the
    ///     Credential Manager on Android) and a Sign in with Apple plugin for iOS.
    ///  4. Add FIREBASE_SDK to Player Settings > Scripting Define Symbols for Android and iOS.
    /// Without FIREBASE_SDK the class runs in development mode: it hands the server a dev token
    /// that the server accepts only when started with its allow-dev-tokens option. Never ship a
    /// build without FIREBASE_SDK.
    /// UNVERIFIED: written against Unity 6 and Firebase Unity SDK 12.x APIs, not compiled here.
    /// </summary>
    public sealed class FirebaseBootstrap : MonoBehaviour
    {
        [SerializeField] private AgeGate ageGate;
        [Tooltip("Development only: token sent when FIREBASE_SDK is not defined.")]
        [SerializeField] private string devToken = "dev:wallet=500";

        public bool Ready { get; private set; }
        public string DisplayName { get; private set; }

        /// <summary>Raised with the session token once sign-in succeeds.</summary>
        public event Action<string> SignedIn;
        public event Action<string> SignInFailed;

#if FIREBASE_SDK
        private FirebaseAuth _auth;
#endif

        private void Start()
        {
#if FIREBASE_SDK
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
            {
                DependencyStatus status = task.Result;
                NetClient.Instance.RunOnMainThread(() =>
                {
                    if (status == DependencyStatus.Available)
                    {
                        _auth = FirebaseAuth.DefaultInstance;
                        Ready = true;
                    }
                    else
                    {
                        Fail("Firebase dependencies unavailable: " + status);
                    }
                });
            });
#else
            Ready = true;
            Debug.LogWarning("FirebaseBootstrap: FIREBASE_SDK not defined, using development login.");
#endif
        }

        /// <summary>Bound to the "Sign in with Google" button.</summary>
        public void SignInWithGoogle()
        {
            if (!CheckAge())
            {
                return;
            }
#if FIREBASE_SDK
            // TODO: obtain the Google ID token from the Google Sign-In plugin, then:
            // Credential credential = GoogleAuthProvider.GetCredential(googleIdToken, null);
            // _auth.SignInWithCredentialAsync(credential).ContinueWith(OnFirebaseSignIn);
            Fail("Google sign-in plugin not integrated yet");
#else
            CompleteDevLogin("Player");
#endif
        }

        /// <summary>Bound to the "Sign in with Apple" button (shown on iOS only).</summary>
        public void SignInWithApple()
        {
            if (!CheckAge())
            {
                return;
            }
#if FIREBASE_SDK
            // TODO: get the Apple identity token and raw nonce from a Sign in with Apple plugin:
            // Credential credential = OAuthProvider.GetCredential("apple.com", appleIdToken, rawNonce, null);
            // _auth.SignInWithCredentialAsync(credential).ContinueWith(OnFirebaseSignIn);
            Fail("Sign in with Apple plugin not integrated yet");
#else
            CompleteDevLogin("Player");
#endif
        }

        private bool CheckAge()
        {
            if (ageGate != null && !ageGate.Confirmed)
            {
                ageGate.Show();
                return false;
            }
            return Ready;
        }

#if FIREBASE_SDK
        private void OnFirebaseSignIn(Task<FirebaseUser> task)
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                NetClient.Instance.RunOnMainThread(() => Fail("Sign-in failed"));
                return;
            }
            FirebaseUser user = task.Result;
            user.TokenAsync(false).ContinueWith(tokenTask =>
            {
                NetClient.Instance.RunOnMainThread(() =>
                {
                    if (tokenTask.IsFaulted || tokenTask.IsCanceled)
                    {
                        Fail("Could not get ID token");
                        return;
                    }
                    DisplayName = string.IsNullOrEmpty(user.DisplayName) ? "Player" : user.DisplayName;
                    // The server verifies this token (TODO on the server: Firebase Admin token
                    // verification, then exchange for a short join ticket because the token is
                    // close to ProtocolConstants.MaxTokenBytes).
                    Connect(tokenTask.Result);
                });
            });
        }
#endif

        private void CompleteDevLogin(string name)
        {
            DisplayName = name;
            Connect(devToken);
        }

        private void Connect(string token)
        {
            if (SignedIn != null)
            {
                SignedIn(token);
            }
            NetClient.Instance.Connect(token, DisplayName);
        }

        private void Fail(string reason)
        {
            // Never log tokens; only the reason.
            Debug.LogWarning("FirebaseBootstrap: " + reason);
            if (SignInFailed != null)
            {
                SignInFailed(reason);
            }
        }
    }
}

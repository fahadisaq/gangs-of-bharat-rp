using System;
using System.Collections.Generic;
using System.Text;

namespace GangsOfBharat.Server
{
    /// <summary>
    /// Minimal word filter for text chat (English, romanized Hindi and Devanagari). Matching words
    /// are replaced with asterisks. This is a placeholder list: the full Hindi/English list and
    /// AI moderation (docs/design/10-communication.md) replace it before beta.
    /// </summary>
    public sealed class AbuseFilter
    {
        private static readonly string[] DefaultWords = new[]
        {
            "fuck", "fucker", "fucking", "shit", "bitch", "bastard", "asshole",
            "chutiya", "madarchod", "bhenchod", "behenchod", "bhosdike", "gandu", "randi", "harami",
            "चूतिया", "मादरचोद", "भेनचोद", "गांडू", "रंडी", "हरामी",
        };

        private readonly HashSet<string> _words;

        public AbuseFilter() : this(DefaultWords)
        {
        }

        public AbuseFilter(IEnumerable<string> words)
        {
            _words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string w in words)
            {
                _words.Add(w);
            }
        }

        private static bool IsWordChar(char c)
        {
            // Devanagari combining marks (matras, virama) are not letters but belong to the word.
            return char.IsLetterOrDigit(c) || (c >= '\u0900' && c <= '\u097F');
        }

        /// <summary>Returns the text with blocked words masked; returns the same instance if clean.</summary>
        public string Filter(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            StringBuilder sb = null;
            int i = 0;
            while (i < text.Length)
            {
                if (!IsWordChar(text[i]))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && IsWordChar(text[i]))
                {
                    i++;
                }
                string word = text.Substring(start, i - start);
                if (_words.Contains(word))
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(text);
                    }
                    for (int k = start; k < i; k++)
                    {
                        sb[k] = '*';
                    }
                }
            }
            return sb == null ? text : sb.ToString();
        }

        /// <summary>Trims, removes control characters and collapses the text to maxChars.</summary>
        public static string Sanitize(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsControl(c))
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }
            string s = sb.ToString().Trim();
            if (s.Length > maxChars)
            {
                s = s.Substring(0, maxChars);
                if (s.Length > 0 && char.IsHighSurrogate(s[s.Length - 1]))
                {
                    s = s.Substring(0, s.Length - 1);
                }
            }
            return s;
        }
    }
}

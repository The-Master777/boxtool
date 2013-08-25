using System;
using System.Collections.Generic;
using System.Text;

namespace FritzBoxApi.Extensions {
    public static class StringBuilderUrlParameterExtension {
        private const int MAXLEN = 32766;

        private const Char PARAM_SEPERATOR = '&';
        private const Char KEY_VALUE_SEPERATOR = '=';

        /// <summary>
        /// Appends a parameter, i.e. a key-value-pair of strings, urlencoded
        /// </summary>
        /// <param name="self">The StringBuilder</param>
        /// <param name="key">The key</param>
        /// <param name="value">The value, urlencoded</param>
        /// <param name="leadingEntry">Is this the leading entry? (Nullable)</param>
        public static void AppendUrlencodedParameter(this StringBuilder self, String key, String value, Boolean? leadingEntry = null) {
            // Add a parameter seperator if this is not the leading entry, or when unset if StringBuilder is empty
            if(leadingEntry.HasValue ? !leadingEntry.Value : self.Length > 0)
                self.Append(PARAM_SEPERATOR);

            // Add key
            self.Append(key);
            self.Append(KEY_VALUE_SEPERATOR);

            // Add value url encoded

            // The number of chars Uri.EscapeDataString can encode at once is limited to MAXLEN, simply add if shorter
            if(value.Length <= MAXLEN) {
                self.Append(Uri.EscapeDataString(value));
                return;
            }

            // otherwise we must split the input-string
            var len = value.Length;

            for(int p = 0; p < len; p += MAXLEN)
                self.Append(Uri.EscapeDataString(value.Substring(p, Math.Min(len - p, MAXLEN))));
        }

        /// <summary>
        /// Appends multiple parameters urlencoded
        /// </summary>
        /// <param name="self">The StringBuilder</param>
        /// <param name="entries">An enumeration of parameters to append</param>
        /// <param name="leadingEntry">Is the first entry also the leading entry?</param>
        public static void AppendUrlencodedParameters(this StringBuilder self, IEnumerable<KeyValuePair<String, String>> entries, Boolean? leadingEntry = null) {
            var first = leadingEntry.HasValue ? leadingEntry.Value : self.Length <= 0;

            // Append each entry
            foreach(var entry in entries) {
                self.AppendUrlencodedParameter(entry.Key, entry.Value, first);

                first = false;
            }
        }
    }
}
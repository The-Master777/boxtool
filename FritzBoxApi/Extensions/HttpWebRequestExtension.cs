using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FritzBoxApi.Extensions {
    public static class HttpWebRequestExtension {
        public static async Task ReadResponseAsync(this HttpWebRequest request, Stream stream) {
            // GetResponseAsync returns a Task<WebResponse>.
            using(var response = await request.GetResponseAsync()) {
                // Get the data stream
                using(var responseStream = response.GetResponseStream()) {
                    if(responseStream == null)
                        throw new IOException("Failed while reading the response data");

                    await responseStream.CopyToAsync(stream); // TODO: Support cancellation
                }
            }
        } 

        public static async Task<Stream> ReadResponseAsync(this HttpWebRequest request) {
            var stream = new MemoryStream();

            await ReadResponseAsync(request, stream);
            stream.Position = 0;

            return stream;
        }

        public static async Task PostUrlencodedAsync(this HttpWebRequest request, IEnumerable<KeyValuePair<String, String>> parameters, Stream stream) {
            var data = FormUrlEncodeParameters(parameters, Encoding.UTF8);

            request.Method = @"POST";
            request.ContentType = @"application/x-www-form-urlencoded";
            request.ContentLength = data.Length;

            using(Stream writeStream = await request.GetRequestStreamAsync()) {
                await writeStream.WriteAsync(data, 0, data.Length);
            }

            // Read response
            await ReadResponseAsync(request, stream);
        }

        public static async Task<Stream> PostUrlencodedAsync(this HttpWebRequest request, IEnumerable<KeyValuePair<String, String>> parameters) {
            var stream = new MemoryStream();

            await PostUrlencodedAsync(request, parameters, stream);
            stream.Position = 0;

            return stream;
        }

        public static String FormUrlEncodeParameters(IEnumerable<KeyValuePair<String, String>> parameters) {
            parameters = parameters ?? new Dictionary<String, String>(); // Tolerate null value

            var sb = new StringBuilder();

            sb.AppendUrlencodedParameters(parameters);

            return sb.ToString();
        }

        public static Byte[] FormUrlEncodeParameters(IEnumerable<KeyValuePair<String, String>> parameters, Encoding encoding) {
            return encoding.GetBytes(FormUrlEncodeParameters(parameters));
        }
    }
}
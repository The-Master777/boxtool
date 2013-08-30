using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace FritzBoxApi.Extensions {
    /// <summary>
    /// The Stream read extension class is providing utility methods for reading a stream's content conveniently
    /// </summary>
    public static class StreamReadExtension {
        /// <summary>
        /// Asynchonously read the <paramref name="stream"/> as String using UTF8 as default encoding, after byteOrderMark-detection of encoding.
        /// </summary>
        /// <param name="stream">The stream to read</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <returns>The stream's content read as string</returns>
        public static async Task<string> ReadStringAsync(this Stream stream, CancellationToken ct) {
            return await ReadStringAsync(stream, Encoding.UTF8, true, ct);
        }

        /// <summary>
        /// Asynchonously read the <paramref name="stream"/> as String using the given <paramref name="encoding"/>.
        /// </summary>
        /// <param name="stream">The stream to read</param>
        /// <param name="encoding">The default encoding to use</param>
        /// <param name="detectEncodingFromByteOrderMarks">Whether to look for byte order marks to detect encoding</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <returns>The stream's content read as string</returns>
        public static async Task<string> ReadStringAsync(this Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, CancellationToken ct) {
            const int BUFFER_SIZE = 4096;

            if(stream == null)
                throw new ArgumentNullException("stream");

            ct.ThrowIfCancellationRequested();

            var ms = new MemoryStream();

            await stream.CopyToAsync(ms, BUFFER_SIZE, ct);

            ms.Position = 0;

            ct.ThrowIfCancellationRequested();

            // Read stream data
            using(var readStream = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks, BUFFER_SIZE))
                return await readStream.ReadToEndAsync(); // Todo: Cancellation support
        }

        /// <summary>
        /// Asynchonously reads XML content of the stream
        /// </summary>
        /// <param name="stream">The stream to read</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <returns>A xml document object</returns>
        public static async Task<XmlDocument> ReadXmlAsync(this Stream stream, CancellationToken ct) {
            var xmlString = await stream.ReadStringAsync(ct); 

            // Parse xml
            var document = new XmlDocument();
            document.LoadXml(xmlString);

            return document;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using FritzBoxApi.Extensions;

namespace FritzBoxApi {

    /// <summary>
    /// The LoginFailedException is an exception to be thrown if the login-process fails.
    /// </summary>
    public class LoginFailedException: Exception {
        protected const String MESSAGE = "The login to Fritz!Box-API has failed";

        public LoginFailedException(): base(MESSAGE) {
        }
    }

    /// <summary>
    /// The InvalidSessionException is an exception to be thrown if the session is invalid, e.g. if rejected by Fritz!Box-Api.
    /// </summary>
    public class InvalidSessionException: Exception {
        public InvalidSessionException(String message): base(message) {
        }
    }
    
    /// <summary>
    /// Provides access to Fritz!Box web api functions for Fritz!OS 5.50 (or newer)
    /// </summary>
    public class Session {
        /// <summary>
        /// IP-Address or Hostname of the Fritz!Box
        /// </summary>
        public Uri Host { get; set; }

        /// <summary>
        /// Password of the api
        /// </summary>
        public String Password { get; set; }

		/// <summary>
		/// Username
		/// </summary>
		public String Username { get; set; }

        /// <summary>
        /// Fritz!Box-Id of the session
        /// </summary>
        public SessionId Id { get; protected set; }

		/*
		 * Docs:
		 * -----
		 * 
		 * [17.12.2012] http://www.avm.de/de/Extern/files/session_id/AVM_Technical_Note_-_Session_ID.pdf
		 * [14.05.2009] http://www.avm.de/de/Extern/Technical_Note_Session_ID.pdf
		 */
        public class SessionId: IEquatable<SessionId> {
            public String Value { get; protected set; }

            /// <summary>
            /// Determine wether Id is valid
            /// </summary>
            public Boolean IsValid { get { return (!ReferenceEquals(this, INVALID)) && ValidateId(Value); } }

            public SessionId(String sid) {
                Value = sid;
            }

            /// <summary>
            /// The invalid / not-set SID is represented by 16 "0"-Characters
            /// </summary>
            protected const string INVALIDSID = @"0000000000000000";

            /// <summary>
            /// Check if the Id given is valid
            /// </summary>
            /// <param name="sid">Id to check</param>
            /// <returns>Validity of the ID</returns>
            protected Boolean ValidateId(String sid) {
                return !(String.IsNullOrEmpty(sid) || sid == INVALIDSID || IsZero(sid));
            }

            /// <summary>
            /// A simple comparison to the number zero
            /// </summary>
            /// <param name="str">String to compare</param>
            /// <returns><c>true</c>, if the string given is zero</returns>
            protected Boolean IsZero(String str) {
                UInt64 number;

                if(!UInt64.TryParse(str, out number))
                    return false;

                return number == 0;
            }

            /// <summary>
            /// Impicit conversion to String by returning Value directly
            /// </summary>
            /// <param name="s">SessionId to convert</param>
            /// <returns>Value of the Id</returns>
            public static implicit operator String(SessionId s) {
                return s.ToString();
            }

            #region Implementation of Equality-Members

            public bool Equals(SessionId other) {
                if(other == null)
                    return false;

                return Value == other.Value;
            }

            public override bool Equals(Object obj) {
                if(obj == null)
                    return false;

                var idObj = obj as SessionId;

                return idObj != null && Equals(idObj);
            }

            public override int GetHashCode() {
                return Value.GetHashCode();
            }

            public static bool operator ==(SessionId a, SessionId b) {
                if(ReferenceEquals(a, b)) return true;
                if(ReferenceEquals(a, null)) return false;
                if(ReferenceEquals(b, null)) return false;

                return a.Equals(b);
            }

            public static bool operator !=(SessionId a, SessionId b) {
                if(ReferenceEquals(a, b)) return false;
                if(ReferenceEquals(a, null)) return true;
                if(ReferenceEquals(b, null)) return true;

                return !(a.Equals(b));
            }

            #endregion

            public override string ToString() {
                return Value;
            }

            /// <summary>
            /// An invalid Id
            /// </summary>
            public static SessionId Invalid { get { return INVALID; } }

            protected static readonly SessionId INVALID = new SessionId(INVALIDSID);
        }

        /// <summary>
        /// Checks if the session has been started
        /// </summary>
        public Boolean IsStarted { get { return Id.IsValid; } }

        /// <summary>
        /// Time of last Api-function invoked
        /// </summary>
        public DateTime LastActionTime { get; protected set; }

        /// <summary>
        /// Idle timeout of the session, the session will be invalidated if an request is made after a greater time-span than <c>IdleTimeout</c> after <see cref="LastActionTime"/>
        /// </summary>
        public TimeSpan IdleTimeout { get; set; }

        /// <summary>
        /// Automatically try to reconnect session if needed
        /// </summary>
        public Boolean AutoReconnect { get; set; }

        protected const String DEFAULT_HOSTNAME = @"fritz.box";
		protected const String DEFAULT_USERNAME = null;
        protected const String HTTP_HOST_FORMAT = "http://{0}/";

        /// <summary>
        /// Creates a new Session with given login-credential and default host
        /// </summary>
        /// <param name="password">The api password</param>
        public Session(String password): this(DEFAULT_HOSTNAME, password) {
        }

        /// <summary>
        /// Creates a new Session with given login-password and specific host address
        /// </summary>
        /// <param name="host">The host address</param>
        /// <param name="password">The api password</param>
		public Session(String host, String password): this(host, DEFAULT_USERNAME, password) {
        }

		/// <summary>
		/// Creates a new Session with given login-cretendial and specific host address
		/// </summary>
		/// <param name="host">The host address</param>
		/// <param name="username">The username</param>
		/// <param name="password">The api password</param>
		public Session(String host, String username, String password): this(new Uri(String.Format(HTTP_HOST_FORMAT, host)), username, password) {
		}

        /// <summary>
        /// Creates a new Session with given login-password and specific host uri
        /// </summary>
        /// <param name="host">The host uri</param>
        /// <param name="password">The api password</param>
		public Session(Uri host, String password): this(host, DEFAULT_USERNAME, password){
        }

		/// <summary>
		/// Creates a new Session with given login-cretendial and specific host uri
		/// </summary>
		/// <param name="host">The host uri</param>
		/// <param name="username">The username</param>
		/// <param name="password">The api password</param>
		public Session(Uri host, String username, String password) {
			Id = SessionId.Invalid;
			Host = host;
			Username = username;
			Password = password;

			IdleTimeout = DEFAULT_IDLE_TIMEOUT;

			LastActionTime = DateTime.MinValue;
		}

        /// <summary>
        /// Asynchronously invalidates the current session state
        /// </summary>
        /// <returns>Validity of the current session</returns>
        public async Task<Boolean> InvalidateAsync(CancellationToken ct) {
            if(!IsStarted)
                return false;

            var xml = await ReadSessionDataAsync(Id, ct);

            var sid = new SessionId(xml.DocumentElement[FIELD_NAME_SID].InnerText);

            LastActionTime = DateTime.Now;

            return sid.IsValid;
        }

        protected const String FIELD_NAME_SID = @"SID";
        protected const String FIELD_NAME_CHALLENGE = @"Challenge";
        protected const String ARG_NAME_RESPONSE = @"response";
		protected const String ARG_NAME_USERNAME = @"username";

        /// <summary>
        /// Asynchronously start a new api session 
        /// </summary>
        public async Task LoginAsync(CancellationToken ct) {
			ct.ThrowIfCancellationRequested();

            // Request login challange
            XmlDocument xml = await ReadSessionDataAsync(Id, ct);

            // We may already be logged in
            var currentSid = new SessionId(xml.DocumentElement[FIELD_NAME_SID].InnerText);

            if(currentSid.IsValid) {
                Id = currentSid;
                LastActionTime = DateTime.Now;

                return;
            }

            // Login
            var challenge = xml.DocumentElement[FIELD_NAME_CHALLENGE].InnerText;
            var responseString = CalculateLoginResponse(challenge, Password);
            
            var uri = new Uri(Host, URL_LOGIN);
            var request = (HttpWebRequest)WebRequest.Create(uri);
            var responseArgs = new Dictionary<String, String> { { ARG_NAME_RESPONSE, responseString } };

			if(!String.IsNullOrEmpty(Username))
				responseArgs.Add(ARG_NAME_USERNAME, Username);
          
            // Quit this method if already cancelled
            ct.ThrowIfCancellationRequested();

            // Register the callback to a method that can unblock. 
            // Dispose of the CancellationTokenRegistration object 
            // after the callback has completed. 
            using(ct.Register(request.Abort)) {
                try {
                    // Store response data in a seperate stream and parse content
                    using(var content = await request.PostUrlencodedAsync(responseArgs)) {
                        xml = await ReadXmlFromStreamAsync(content, ct);
                    }
                } catch(WebException e) {
                    if(e.Status == WebExceptionStatus.RequestCanceled)
                        ct.ThrowIfCancellationRequested();

                    throw;
                }
            }

            var sid = new SessionId(xml.DocumentElement[FIELD_NAME_SID].InnerText);

            if(!sid.IsValid) 
                throw new LoginFailedException();

            Id = sid;
            LastActionTime = DateTime.Now;
        }

        /// <summary>
        /// Asynchronously finalize the api session
        /// </summary>
        /// <returns>Success?</returns>
        public async Task<Boolean> LogoutAsync(CancellationToken ct) {
            await ForceSessionAsync(ct);

            var uri = new Uri(Host, URL_HOME + String.Format(SID_ARG_FORMAT, Id));
            var request = (HttpWebRequest)WebRequest.Create(uri);
            var responseArgs = new Dictionary<String, String> {{@"logout", @"1"}};

            // Quit this method if already cancelled
            ct.ThrowIfCancellationRequested();

            // Register the callback to a method that can unblock. 
            // Dispose of the CancellationTokenRegistration object 
            // after the callback has completed. 
            using(ct.Register(request.Abort)) {

                try {

                    // Store response data in a seperate stream and parse content
                    using(var content = await request.PostUrlencodedAsync(responseArgs)) {
                        // Read stream data
                        String text = await ReadStream(content, Encoding.UTF8, true, ct); 

                        LastActionTime = DateTime.MinValue;
                        Id = SessionId.Invalid;

                        return text.Contains(@"/login.lua");
                    }
                } catch(WebException e) {
                    if(e.Status == WebExceptionStatus.RequestCanceled)
                        ct.ThrowIfCancellationRequested();

                    throw;
                }
            }
        }

        /// <summary>
        /// Asynchronously query a command-parameter
        /// </summary>
        /// <param name="item">The command-parameter to query</param>
        /// <param name="ct">The CancellationToken used to cancel the async method</param>
        /// <returns>Value of the queried command</returns>
        public async Task<String> QueryAsync(String item, CancellationToken ct) {
            return (await QueryAsync(ct, new [] { item })).First();
        }

        /// <summary>
        /// Asynchronously query multiple command-parameters
        /// </summary>
        /// <param name="items">The command-parameters to query</param>
        /// <param name="ct">The CancellationToken used to cancel the async method</param>
        /// <returns>Values of the queried commands</returns>
        public async Task<IEnumerable<String>> QueryAsync(CancellationToken ct, params String[] items) {
            return await QueryAsync(items as IEnumerable<String>, ct);
        }

        protected static readonly Regex QUERY_JSON_RESPONSE_ITEM_REGEX = new Regex(@"""a(?<id>\d+)"": ""(?<value>.*?)"",?");

        /// <summary>
        /// Asynchronously query multiple command-parameters
        /// </summary>
        /// <param name="items">The command-parameters to query</param>
        /// <param name="ct">The CancellationToken used to cancel the async method</param>
        /// <param name="progress">The progress update handler</param>
        /// <returns>Values of the queried commands</returns>
        public async Task<IEnumerable<String>> QueryAsync(IEnumerable<String> items, CancellationToken ct, IProgress<Tuple<int, int>> progress = null) {
            var urlStringBase = URL_QUERY + String.Format(SID_ARG_FORMAT, Id);

            int count;

            var partitions = PartitionQueryItems(items, urlStringBase, out count);

            var resultDict = new ConcurrentDictionary<UInt64, String>();

            var pp = partitions.Select(partition => LoadPartition(partition, urlStringBase, resultDict, ct, progress, count));

            await ForceSessionAsync(ct);

            /*
            foreach(var task in pp) {
                await task;
            }*/

            await Task.Factory.StartNew(() => {
                try {
                    Parallel.ForEach(pp, new ParallelOptions {CancellationToken = ct, MaxDegreeOfParallelism = 4}, task => {
                        try {
                            task.Wait(ct);
                        } catch(OperationCanceledException oce) {
                            if(oce.CancellationToken != ct)
                                throw;
                        }
                    });
                } catch(OperationCanceledException oce) {
                    if(oce.CancellationToken != ct)
                        throw;
                }
            }, ct);

            ct.ThrowIfCancellationRequested();

            /*
            
            foreach(var partition in partitions) {
                var uri = new Uri(Host, urlStringBase + @"&" +  HttpWebRequestExtension.FormUrlEncodeParameters(partition));
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = HTTP_METHOD_GET;

                // Store response data in a seperate stream
                using(var content = await request.ReadResponseAsync()) {
                    LastActionTime = DateTime.Now;

                    String text;

                    // Read stream data
                    using(var readStream = new StreamReader(content, Encoding.UTF8, true))
                        text = await readStream.ReadToEndAsync();

                    // Parse JSON Items like
                    //   "a0": "74.05.50"
                    foreach(Match match in QUERY_JSON_RESPONSE_ITEM_REGEX.Matches(text)) {
                        UInt64 p;

                        if(!UInt64.TryParse(match.Groups[@"id"].Value, out p))
                            continue;

                        if(resultDict.ContainsKey(p))
                            throw new Exception("Duplicate element");

                        resultDict.TryAdd(p, match.Groups[@"value"].Value);
                    }
                }
            }*/

            // Return elements ordered by number
            return resultDict.Keys.OrderBy(key => key).Select(key => resultDict[key]);

            /*await ForceSessionAsync();

            var args = new Dictionary<String, String>();

            int i = 0;
            foreach(var item in items) {
                args.Add(String.Format("a{0}", i), item);
                i++;
            }

            // TODO: Chop GET-uri to a length < 8190

            var resultDict = new Dictionary<UInt64, String>(args.Count);

            var uri = new Uri(Host, URL_QUERY + String.Format(SID_ARG_FORMAT + "&{1}", Id, HttpWebRequestExtension.FormUrlEncodeParameters(args)));
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = HTTP_METHOD_GET;

            // Store response data in a seperate stream
            using(var content = await request.ReadResponseAsync()) {
                LastActionTime = DateTime.Now;

                String text;

                // Read stream data
                using(var readStream = new StreamReader(content, Encoding.UTF8, true))
                    text = await readStream.ReadToEndAsync();

                // Parse JSON Items like
                //   "a0": "74.05.50"
                foreach(Match match in QUERY_JSON_RESPONSE_ITEM_REGEX.Matches(text)) {
                    UInt64 p;

                    if(!UInt64.TryParse(match.Groups[@"id"].Value, out p))
                        continue;

                    if(resultDict.ContainsKey(p))
                        throw new Exception("Duplicate element");

                    resultDict.Add(p, match.Groups[@"value"].Value);
                }
            }

            // Return elements ordered by number
            return resultDict.Keys.OrderBy(ki => ki).Select(ki => resultDict[ki]);*/
        }

        protected const int DEFAULT_MAX_LENGTH = 2083; // Used by IE, should be safe limit

        protected IEnumerable<IDictionary<String, String>> PartitionQueryItems(IEnumerable<String> items, String prefix, out int count, int length = DEFAULT_MAX_LENGTH) {
            var result = new List<IDictionary<String, String>>();

            var sb = new StringBuilder();

            int i = 0;
            Dictionary<String, String> current = null;

            foreach(var item in items) {
                int k = i++;

                while(true) {
                    if(current == null || sb.Length >= length) {
                        result.Add(current = new Dictionary<String, String>());
                        sb.Clear();
                        sb.Append(prefix);
                    }

                    String key = String.Format(@"a{0}", k);

                    sb.AppendUrlencodedParameter(key, item, false);

                    if(sb.Length >= length) 
                        continue; // TODO: This will lead to an infinite loop if |prefix + "&" + Urlencode(key + "=" + item)| > length

                    current.Add(key, item);

                    break;
                }
            }

            count = i;

            return result;
        }

        protected async Task LoadPartition(IDictionary<String, String> partition, String urlStringBase, ConcurrentDictionary<UInt64, String> resultDict, CancellationToken ct, IProgress<Tuple<int,int>> progress, int total) {
            var uri = new Uri(Host, urlStringBase + @"&" + HttpWebRequestExtension.FormUrlEncodeParameters(partition));
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = HTTP_METHOD_GET;

            // Quit this method if already cancelled
            ct.ThrowIfCancellationRequested();

            // Register the callback to a method that can unblock. 
            // Dispose of the CancellationTokenRegistration object 
            // after the callback has completed. 
            using(ct.Register(request.Abort)) {

                try {
                    // Store response data in a seperate stream
                    using(var content = await request.ReadResponseAsync()) {
                        LastActionTime = DateTime.Now;

                        // Read stream data
                        String text = await ReadStream(content, Encoding.UTF8, true, ct); 

                        // Parse JSON Items like
                        //   "a0": "74.05.50"
                        foreach(Match match in QUERY_JSON_RESPONSE_ITEM_REGEX.Matches(text)) {
                            UInt64 p;

                            if(!UInt64.TryParse(match.Groups[@"id"].Value, out p))
                                continue;

                            if(resultDict.ContainsKey(p))
                                throw new Exception("Duplicate element");

                            if(resultDict.TryAdd(p, match.Groups[@"value"].Value) && progress != null)
                                progress.Report(Tuple.Create(resultDict.Count, total));
                        }
                    }
                } catch(WebException e) {
                    if(e.Status == WebExceptionStatus.RequestCanceled)
                        ct.ThrowIfCancellationRequested();

                    throw;
                }
            }
        }

        protected ApiQueryMarshaller ObjectQueryMarshaller;

        public async Task QueryAsync(Object queryObject, CancellationToken ct, IProgress<Tuple<int, int>> progress = null) {
            if(ObjectQueryMarshaller == null)
                ObjectQueryMarshaller = new ApiQueryMarshaller(this);

            await ObjectQueryMarshaller.QueryAsync(queryObject, ct, progress);
        }

        /// <summary>
        /// Asynchonously execute commands
        /// </summary>
        /// <param name="commands">The commands to execute</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <returns>The api response-text</returns>
        public async Task<String> SendCommandsAsync(IEnumerable<KeyValuePair<String, String>> commands, CancellationToken ct) {
            await ForceSessionAsync(ct);

            var uri = new Uri(Host, URL_WEBCM);
            var request = (HttpWebRequest)WebRequest.Create(uri);
            var responseArgs = new Dictionary<String, String> { { @"sid", Id } };

            // Copy over commands
            foreach(var command in commands) {
                responseArgs.Add(command.Key, command.Value);
            }

            // Store response data in a seperate stream and read to end
            String responseText;

            
            // Quit this method if already cancelled
            ct.ThrowIfCancellationRequested();

            // Register the callback to a method that can unblock. 
            // Dispose of the CancellationTokenRegistration object 
            // after the callback has completed. 
            using(ct.Register(request.Abort)) {
                try {
                    using(var content = await request.PostUrlencodedAsync(responseArgs)) {
                        LastActionTime = DateTime.Now;

                        // Read stream data
                        responseText = await ReadStream(content, Encoding.UTF8, true, ct);
                    }
                } catch(WebException e) {
                    if(e.Status == WebExceptionStatus.RequestCanceled)
                        ct.ThrowIfCancellationRequested();

                    throw;
                }
            }

            return responseText;
        }

        protected static async Task<String> ReadStream(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, CancellationToken ct) {

            ct.ThrowIfCancellationRequested();

            var ms = new MemoryStream();

            await stream.CopyToAsync(ms, 4096, ct);

            ms.Position = 0;

            ct.ThrowIfCancellationRequested();

            // Read stream data
            using(var readStream = new StreamReader(ms, Encoding.UTF8, true))
                return await readStream.ReadToEndAsync(); // Todo: Cancellation support
        }

        /// <summary>
        /// Asynchonously force the session to be started, and invalidate session if idle; e.g. when <see cref="LastActionTime"/> is longer than <see cref="IdleTimeout"/> ago.
        /// </summary>
        protected async Task ForceSessionAsync(CancellationToken ct) {
            if(!IsStarted)
                throw new InvalidSessionException("Session not started");

            if(IdleTimeout > TimeSpan.Zero && DateTime.Now - LastActionTime > IdleTimeout) {
                // Check timeout
                if(await InvalidateAsync(ct))
                    return;

                if(!AutoReconnect)
                    throw new InvalidSessionException("Session timed out");

                await LoginAsync(ct);
            }
        }

        /// <summary>
        /// The default idle timeout is 10 minutes
        /// </summary>
        protected static readonly TimeSpan DEFAULT_IDLE_TIMEOUT = TimeSpan.FromMinutes(10);

        protected const String URL_LOGIN = @"/login_sid.lua";
        protected const String URL_HOME  = @"/home/home.lua";
        protected const String URL_QUERY = @"/query.lua";
        protected const String URL_WEBCM = @"/cgi-bin/webcm";

        protected const String SID_ARG_FORMAT = @"?sid={0}";

        protected const String HTTP_METHOD_GET  = @"GET";
        protected const String HTTP_METHOD_POST = @"POST";

        protected async Task<XmlDocument> ReadSessionDataAsync(SessionId id, CancellationToken ct) {
            // Initialize a GET Http request for the login Uri
            var uri = new Uri(Host, URL_LOGIN + (id.IsValid ? String.Format(SID_ARG_FORMAT, id) : String.Empty));
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = HTTP_METHOD_GET;
            
            // Quit this method if already cancelled
            ct.ThrowIfCancellationRequested();

            // Register the callback to a method that can unblock. 
            // Dispose of the CancellationTokenRegistration object 
            // after the callback has completed. 
            using(ct.Register(request.Abort)) {
                try {

                    // Store response data in a seperate stream
                    using(var content = await request.ReadResponseAsync()) {
                        return await ReadXmlFromStreamAsync(content, ct);
                    }

                } catch(WebException e) {
                    if(e.Status == WebExceptionStatus.RequestCanceled)
                        ct.ThrowIfCancellationRequested();

                    throw;
                }
            }
        }

        /// <summary>
        /// Asynchonously reads XML content from the stream given
        /// </summary>
        /// <param name="stream">The stream to read</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <returns>A xml document object</returns>
        protected static async Task<XmlDocument> ReadXmlFromStreamAsync(Stream stream, CancellationToken ct) {
            String xmlString = await ReadStream(stream, Encoding.UTF8, true, ct); 

            // Parse xml
            var document = new XmlDocument();
            document.LoadXml(xmlString);

            return document;
        }

        protected static String CalculateLoginResponse(String challenge, String password) {
            const String COMBINE_FORMAT = @"{0}-{1}";
            const Char NON_ANSI_REPLACEMENT = '.';

            // Convert all non ANSI-Chars
            var pwchars = password.ToCharArray();

            for(int i = 0; i < pwchars.Length; i++)
                if(pwchars[i] > 255)
                    pwchars[i] = NON_ANSI_REPLACEMENT;

            password = new String(pwchars);

            // http://www.avm.de/de/Extern/Technical_Note_Session_ID.pdf - UTF16LE
            var hash = new MD5CryptoServiceProvider().ComputeHash(Encoding.Unicode.GetBytes(String.Format(COMBINE_FORMAT, challenge, password)));

            return String.Format(COMBINE_FORMAT, challenge, String.Concat(Array.ConvertAll(hash, x => x.ToString(@"x2")))); // Lowercase Base 16
        }
    }
}

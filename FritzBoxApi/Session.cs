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

        /// <summary>
        /// Checks if the session has been started
        /// </summary>
        public Boolean IsStarted { get { return Id.IsValid; } }

        /// <summary>
        /// Time of last Api-function invoked
        /// </summary>
        public LastActionTimer LastActionTime { get; protected set; }

        /// <summary>
        /// Idle timeout of the session, the session will be invalidated if an request is made after a greater time-span than <c>IdleTimeout</c> after <see cref="LastActionTime"/>
        /// </summary>
        public TimeSpan IdleTimeout { get; set; }

        /// <summary>
        /// Automatically try to reconnect session if needed
        /// </summary>
        public Boolean AutoReconnect { get; set; }

        /// <summary>
        /// The default api query marshaller to use
        /// </summary>
        protected readonly Lazy<ApiQueryMarshaller> ObjectQueryMarshaller;

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

            ObjectQueryMarshaller = new Lazy<ApiQueryMarshaller>(() => new ApiQueryMarshaller(this), LazyThreadSafetyMode.PublicationOnly);

			LastActionTime = new LastActionTimer();
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

            LastActionTime.Update();

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
                LastActionTime.Update();

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
                        xml = await content.ReadXmlAsync(ct);
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
            LastActionTime.Update();
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

            // Register the callback to a method that can unblock. 
            // Dispose of the CancellationTokenRegistration object 
            // after the callback has completed. 
            using(ct.Register(request.Abort)) {
                try {
                    // Store response data in a seperate stream and parse content
                    using(var content = await request.PostUrlencodedAsync(responseArgs)) {
                        // Read stream data
                        String text = await content.ReadStringAsync(ct);

                        LastActionTime.Reset();
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

        /// <summary>
        /// Asynchronously query multiple command-parameters
        /// </summary>
        /// <param name="items">The command-parameters to query</param>
        /// <param name="ct">The CancellationToken used to cancel the async method</param>
        /// <param name="progress">The progress update handler</param>
        /// <returns>Values of the queried commands</returns>
        public async Task<IEnumerable<String>> QueryAsync(IEnumerable<String> items, CancellationToken ct, IProgress<Tuple<int, int>> progress = null) {
            await ForceSessionAsync(ct);

            var urlStringBase = URL_QUERY + String.Format(SID_ARG_FORMAT, Id);

            int count;

            var partitions = PartitionQueryItems(items, urlStringBase, out count);

            var resultDict = new ConcurrentDictionary<UInt64, String>();

            var pp = partitions.Select(partition => LoadPartition(partition, urlStringBase, resultDict, ct, progress, count));


            // Process up to 4 requests in parallel
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

            // Return elements ordered by number
            return resultDict.Keys.OrderBy(key => key).Select(key => resultDict[key]);
        }

        /// <summary>
        /// A safe limit for <see cref="PartitionQueryItems"/>
        /// </summary>
        protected const int DEFAULT_PQI_MAX_LENGTH = 2083; // Used by IE, should be safe limit

        /// <summary>
        /// Partitions items used for query in groups of appropriate length, according to the their urlencoded query string being shorter than <see cref="DEFAULT_PQI_MAX_LENGTH"/> (typically a safe limit like <c>2083</c> characters)
        /// </summary>
        /// <param name="items">The items to partition</param>
        /// <param name="prefix">The prefix string used to determine the absolute query string length</param>
        /// <param name="count">The number of items</param>
        /// <param name="length">The maximum length</param>
        /// <returns>An enumeration of paritions</returns>
        protected IEnumerable<IDictionary<String, String>> PartitionQueryItems(IEnumerable<String> items, String prefix, out int count, int length = DEFAULT_PQI_MAX_LENGTH) {
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

                    var key = String.Format(@"a{0}", k);

                    sb.AppendUrlencodedParameter(key, item, false);

                    if(sb.Length >= length) {
                        // If unchecked, this would lead to an infinite loop if |prefix + "&" + Urlencode(key + "=" + item)| >= length
                        if(current.Count <= 0)
                            throw new Exception("Query parameter too long");

                        continue; 
                    }

                    current.Add(key, item);

                    break;
                }
            }

            count = i;

            return result;
        }

        protected static readonly Regex QUERY_JSON_RESPONSE_ITEM_REGEX = new Regex(@"""a(?<id>\d+)"": ""(?<value>.*?)"",?");

        /// <summary>
        /// Asynchronously loads a query-item partition
        /// </summary>
        /// <param name="partition">The partition to load</param>
        /// <param name="urlStringBase">The base url string</param>
        /// <param name="resultDict">The result dictionary</param>
        /// <param name="ct">The CancellationToken used to cancel this async operation</param>
        /// <param name="progress">The progress update handler</param>
        /// <param name="total">The total number of items, needed for progress calculation</param>
        /// <returns>A task performing this operation</returns>
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
                        LastActionTime.Update();

                        // Read stream data
                        var text = await content.ReadStringAsync(ct); 

                        // Parse JSON Items like
                        //   "a0": "74.05.50"
                        foreach(Match match in QUERY_JSON_RESPONSE_ITEM_REGEX.Matches(text)) {
                            UInt64 p;

                            if(!UInt64.TryParse(match.Groups[@"id"].Value, out p))
                                throw new Exception("Invalid argument number");

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

        /// <summary>
        /// Query all members of <paramref name="queryObject"/> according to the behaviour of <see cref="ApiQueryMarshaller"/> asynchonously.
        /// <p>All members having a <see cref="QueryParameterAttribute"/> or <see cref="QueryPropagationAttribute"/> are queried using the api.</p>
        /// </summary>
        /// <param name="queryObject">The object to query</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <param name="progress">Progress updates are reported using this provider for progress updates if set</param>
        /// <returns>A task performing this operation</returns>
        public async Task QueryAsync(Object queryObject, CancellationToken ct, IProgress<Tuple<int, int>> progress = null) {
            await ObjectQueryMarshaller.Value.QueryAsync(queryObject, ct, progress);
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
                        LastActionTime.Update();

                        // Read stream data
                        responseText = await content.ReadStringAsync(ct);
                    }
                } catch(WebException e) {
                    if(e.Status == WebExceptionStatus.RequestCanceled)
                        ct.ThrowIfCancellationRequested();

                    throw;
                }
            }

            return responseText;
        }

        /// <summary>
        /// Asynchonously force the session to be started, and invalidate session if idle; e.g. when <see cref="LastActionTime"/> is longer than <see cref="IdleTimeout"/> ago.
        /// </summary>
        protected async Task ForceSessionAsync(CancellationToken ct) {
            // Quit this method if already cancelled
            ct.ThrowIfCancellationRequested();

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
        /// <p>A 30 second buffer is used "just to be sure"</p>
        /// </summary>
        protected static readonly TimeSpan DEFAULT_IDLE_TIMEOUT = TimeSpan.FromMinutes(9.5);

        protected const String URL_LOGIN = @"/login_sid.lua";
        protected const String URL_HOME  = @"/home/home.lua";
        protected const String URL_QUERY = @"/query.lua";
        protected const String URL_WEBCM = @"/cgi-bin/webcm";

        protected const String SID_ARG_FORMAT = @"?sid={0}";

        protected const String HTTP_METHOD_GET  = @"GET";
        protected const String HTTP_METHOD_POST = @"POST";

        /// <summary>
        /// Asynchonously read the <c>login_sid.lua</c> file with specific <see cref="SessionId"/> (defined by <paramref name="id"/>) and return the parsed XML-Document.
        /// </summary>
        /// <param name="id">The session-id to use</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <returns>The parsed response as XML-Document</returns>
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
                        return await content.ReadXmlAsync(ct);
                    }

                } catch(WebException e) {
                    if(e.Status == WebExceptionStatus.RequestCanceled)
                        ct.ThrowIfCancellationRequested();

                    throw;
                }
            }
        }

        /// <summary>
        /// Calculate the response value using the <paramref name="challenge"/> and <paramref name="password"/>
        /// </summary>
        /// <param name="challenge">The challenge</param>
        /// <param name="password">The password</param>
        /// <returns>The response</returns>
        /*
         * See: 
         *   - http://www.avm.de/de/Extern/files/session_id/AVM_Technical_Note_-_Session_ID.pdf
         *   - http://www.avm.de/de/Extern/Technical_Note_Session_ID.pdf
         * 
         * Der MD5-Hash wird über die Bytefolge der UTF-16LE-Codierung dieses 
         * Strings gebildet (ohne BOM und ohne abschließende 0-Bytes). 
         * Aus Kompatibilitätsgründen muss für jedes Zeichen, dessen Unicode Codepoint > 255 ist, die 
         * Codierung des "."-Zeichens benutzt werden (0x2e 0x00 in UTF-16LE). Dies betriﬀt also alle 
         * Zeichen, die nicht in ISO-8859-1 dargestellt werden können, z. B. das Euro-Zeichen.
         */
        protected static String CalculateLoginResponse(String challenge, String password) {
            const String COMBINE_FORMAT = @"{0}-{1}";
            const Char NON_ANSI_REPLACEMENT = '.';

            // Convert all non ANSI-Chars
            var pwchars = password.ToCharArray();

            for(int i = 0; i < pwchars.Length; i++)
                if(pwchars[i] > 255)
                    pwchars[i] = NON_ANSI_REPLACEMENT;

            password = new String(pwchars);

            // Encoding.Unicode = UTF16LE
            var hash = new MD5CryptoServiceProvider().ComputeHash(Encoding.Unicode.GetBytes(String.Format(COMBINE_FORMAT, challenge, password)));

            return String.Format(COMBINE_FORMAT, challenge, String.Concat(Array.ConvertAll(hash, x => x.ToString(@"x2")))); // Lowercase Base 16
        }

        #region Inner classes

        /*
         * Docs:
         * -----
         * 
         * [17.12.2012] http://www.avm.de/de/Extern/files/session_id/AVM_Technical_Note_-_Session_ID.pdf
         * [14.05.2009] http://www.avm.de/de/Extern/Technical_Note_Session_ID.pdf
         */
        /// <summary>
        /// A class used provide utility functions for session-id-usage
        /// </summary>
        public class SessionId : IEquatable<SessionId> {
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
        /// A class used for encapsulation of the time of last action performed
        /// </summary>
        public class LastActionTimer {
            /// <summary>
            /// The actual value
            /// </summary>
            private DateTime _value;

            /// <summary>
            /// A ReaderWriter-Lock used to ensure thread safety
            /// </summary>
            protected ReaderWriterLockSlim Lock { get; private set; }

            /// <summary>
            /// The default value set after a reset operation
            /// </summary>
            protected static readonly DateTime RESET_VALUE = DateTime.MinValue;

            /// <summary>
            /// Instantiates a new <see cref="LastActionTimer"/> in reset state.
            /// </summary>
            public LastActionTimer(): this(RESET_VALUE) {
            }

            /// <summary>
            /// Instantiates a new <see cref="LastActionTimer"/> in state defined by <paramref name="value"/>.
            /// </summary>
            /// <param name="value">The value to set</param>
            public LastActionTimer(DateTime value) {
                _value = value;
                Lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            }

            /// <summary>
            /// The value hold
            /// </summary>
            public DateTime Value {
                get {
                    Lock.EnterReadLock();
                    try {
                        return _value;
                    } finally {
                        Lock.ExitReadLock();
                    }
                }

                protected set {
                    Lock.EnterWriteLock();
                    try {
                        _value = value;
                    } finally {
                        Lock.ExitWriteLock();
                    }
                }
            }

            /// <summary>
            /// Implicit conversion to DateTime by directly returning the value hold
            /// </summary>
            /// <param name="t"></param>
            /// <returns></returns>
            public static implicit operator DateTime(LastActionTimer t) {
                return t.Value;
            }

            /// <summary>
            /// Update to current time
            /// </summary>
            public void Update() {
                Value = DateTime.Now;
            }

            /// <summary>
            /// Reset value
            /// </summary>
            public void Reset() {
                Value = RESET_VALUE;
            }
        }

        #endregion
    }
}

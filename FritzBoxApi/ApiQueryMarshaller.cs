using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FritzBoxApi.Extensions;

namespace FritzBoxApi {
    /// <summary>
    /// <p>A class for marshalling the query items of a declarative query definition into a real api query.</p><br/><br/>
    /// 
    /// <p>The attributes <see cref="QueryParameterAttribute"/> and <see cref="QueryPropagationAttribute"/> are used to declare members of an object as query items.</p><br/>
    /// <p>The attribute <see cref="QueryValueConverter"/> is used when defining a custom value converter. (e.g. for conversion of String to Integer)</p><br/>
    /// <p>Example of declaration: 
    /// <code>
    /// /* Definition of an example query object */
    /// public class ExampleQueryObject {
    ///     /* Definition of a String -> Int converter named 'IntConverter' */
    ///     [QueryValueConverter]
    ///     protected readonly Func&lt;String, Object&gt; IntConverter = (s => String.IsNullOrEmpty(s) || s.Equals(@"er") ? -1 : int.Parse(s));
    /// 
    ///     /* Declaration of the query item 'logic:status/nspver' in field 'Firmware' */
    ///     [QueryParameter(@"logic:status/nspver")]
    ///     public String Firmware { get; set; }
    /// 
    ///     /* 
    ///      * Declaration of the query item 'sar:status/dsl_ds_rate' in field 'DsDataRate' 
    ///      * using the defined String -> Int converter 'IntConverter' for automatic 
    ///      * conversion of the queried value
    ///      */
    ///     [QueryParameter(@"sar:status/dsl_ds_rate", "IntConverter")]
    ///     public int DsDataRate { get; set; }
    /// }
    /// </code></p><br/>
    /// <p>Example of usage: <br/>
    /// <code>
    /// var query = new ExampleQueryObject();
    /// Session session = ...;
    /// 
    /// /* Option 1 - ApiQueryMarshaller */
    /// await (new ApiQueryMarshaller(session)).QueryAsync(query, CancellationToken.None);
    /// 
    /// /* Option 2 - via Session */
    /// await session.QueryAsync(query, CancellationToken.None);
    /// 
    /// /*
    ///  * Either way you choose, the query object will be loaded with the items declared.
    ///  *
    ///  * You can use the field as normal (changes won't propagate back to the api)
    ///  */
    /// System.Diagnostics.Debug.WriteLine(query.Firmware); // ...
    /// </code></p>
    /// </summary>
    public class ApiQueryMarshaller {
        /// <summary>
        /// The session used by this <see cref="ApiQueryMarshaller"/>
        /// </summary>
        public Session Api { get; protected set; }

        /// <summary>
        /// Instantiates a new <see cref="ApiQueryMarshaller"/> using the given session
        /// </summary>
        /// <param name="api">The session to use</param>
        public ApiQueryMarshaller(Session api) {
            if(api == null)
                throw new ArgumentNullException("api");

            Api = api;
        }

        /// <summary>
        /// Query all members of <paramref name="queryObject"/> asynchonously.
        /// <p>All members having a <see cref="QueryParameterAttribute"/> or <see cref="QueryPropagationAttribute"/> are queried using the api.</p>
        /// </summary>
        /// <param name="queryObject">The object to query</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <param name="progress">Progress updates are reported using this provider for progress updates if set</param>
        /// <returns>A task performing this operation</returns>
        public async Task QueryAsync(Object queryObject, CancellationToken ct, IProgress<Tuple<int, int>> progress = null) {
            var d = CollectParameters(queryObject).ToList();

            ct.ThrowIfCancellationRequested();

            // Run query
            var response = (await Api.QueryAsync(d.Select(x => x.Attribute.Command), ct, progress)).ToList();

            // Process response
            if(response.Count != d.Count)
                throw new Exception("Invalid response");

            for(int i = 0; i < d.Count; i++) {
                // Set new value 
                var m = d[i].Member;

                var v = d[i].Convert(response[i]);

                // Set value
                if(m is PropertyInfo) {
                    (m as PropertyInfo).SetValue(d[i].Object, v);
                } else if(m is FieldInfo) {
                    (m as FieldInfo).SetValue(d[i].Object, v);
                }
            }
        }

        #region Helper functions

        /// <summary>
        /// Collects all suitable parameters of an object and its members marked with <see cref="QueryPropagationAttribute"/>
        /// </summary>
        /// <param name="object">The source object</param>
        /// <returns>An enumeration of <see cref="QueryParameterPropertyRecord"/>s</returns>
        protected static IEnumerable<QueryParameterPropertyRecord> CollectParameters(Object @object) {
            var list = new List<QueryParameterPropertyRecord>();

            CollectParameters(@object, list);

            return list;
        }

        /// <summary>
        /// Collects all suitable parameters of an object and its members marked with <see cref="QueryPropagationAttribute"/>
        /// </summary>
        /// <param name="object">The source object</param>
        /// <param name="qpprList">The result list</param>
        protected static void CollectParameters(Object @object, List<QueryParameterPropertyRecord> qpprList) {
            const BindingFlags RELEVANT_MEMBERS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            // A) Collect all members
            var members = @object.GetType().GetProperties(RELEVANT_MEMBERS).Concat<MemberInfo>(@object.GetType().GetFields(RELEVANT_MEMBERS)).ToList();

            // B.1) Find all converters
            var converterMembers = members.Where(x => Attribute.IsDefined(x, typeof(QueryValueConverter), true));

            // B.2) Build converter dictionary
            var converters = converterMembers.ToDictionary(m => {
                var n = m.GetCustomAttribute<QueryValueConverter>().Name;

                if(String.IsNullOrEmpty(n))
                    n = m.Name;

                return n;
            } , m => GetMemberValue<Func<String, object>>(@object, m));

            // C.1) Filter query parameters properties
            var queryParameterProps = members.Where(x => Attribute.IsDefined(x, typeof(QueryParameterAttribute), true));

            // C.2) Build query list
            var d = queryParameterProps.Select(m => new QueryParameterPropertyRecord(@object, m.GetCustomAttribute<QueryParameterAttribute>(), m, converters));

            qpprList.AddRange(d);

            // D) Collect all sub-element parameters
            var subMems = members.Where(x => Attribute.IsDefined(x, typeof(QueryPropagationAttribute), true));

            foreach(var sm in subMems) {
                CollectParameters(GetMemberValue(@object, sm), qpprList);
            }
        }
        
        /// <summary>
        /// Retrieves the value of the given member, casting to type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">The type of the retrieved value</typeparam>
        /// <param name="object">The object</param>
        /// <param name="member">The member</param>
        /// <returns>The value</returns>
        protected static T GetMemberValue<T>(Object @object, MemberInfo member) {
            return (T)GetMemberValue(@object, member);
        }

        /// <summary>
        /// Retrieves the value of the given member
        /// </summary>
        /// <param name="object">The object</param>
        /// <param name="member">The member</param>
        /// <returns>The value</returns>
        protected static Object GetMemberValue(Object @object, MemberInfo member) {
            if(member is PropertyInfo)
                return ((member as PropertyInfo).GetValue(@object));

            if(member is FieldInfo)
                return ((member as FieldInfo).GetValue(@object));

            throw new Exception("MemberInfo is not supported");
        }

        #endregion

        #region Inner classes

        /// <summary>
        /// A class for storing the parameter, the member, it's attribute, and the source object
        /// </summary>
        protected class QueryParameterPropertyRecord {
            /// <summary>
            /// The source object
            /// </summary>
            public Object Object { get; set; }

            /// <summary>
            /// The <see cref="Member"/>'s <see cref="QueryParameterAttribute"/>
            /// </summary>
            public QueryParameterAttribute Attribute { get; set; }

            /// <summary>
            /// The member
            /// </summary>
            public MemberInfo Member { get; set; }

            /// <summary>
            /// The known converters
            /// </summary>
            public Dictionary<String, Func<String, object>> Converters;

            /// <summary>
            /// The Identity-Converter f(x) => x
            /// </summary>
            public static readonly Func<String, String> IDENTITY = (x => x);

            public QueryParameterPropertyRecord(Object @object, QueryParameterAttribute attribute, MemberInfo member, Dictionary<String, Func<String, object>> converters) {
                Object = @object;
                Attribute = attribute;
                Member = member;
                Converters = converters;
            }

            /// <summary>
            /// This property selects the converter to use, a <see cref="ConverterNotFoundException"/> is 
            /// thrown if no appropriate converter could be selected.
            /// </summary>
            public Func<String, object> Converter {
                get {
                    if(Converters == null || String.IsNullOrEmpty(Attribute.Converter))
                        return IDENTITY;
                    
                    if(!Converters.ContainsKey(Attribute.Converter))
                        throw new ConverterNotFoundException(Converters, Attribute.Converter);

                    return Converters[Attribute.Converter];
                }
            } 

            /// <summary>
            /// Trys to convert the input using the selected converter
            /// </summary>
            /// <param name="input">The input to convert</param>
            /// <returns>The conversion result</returns>
            public object Convert(String input) {
                return Converter(input);
            }
        }

        #endregion
    }

    /// <summary>
    /// An exception to be thrown if case of a missing converter
    /// </summary>
    public class ConverterNotFoundException : Exception {
        public Dictionary<String, Func<String, object>> Converters { get; protected set; }
        public String Converter { get; set; }

        protected const String MESSAGE = "The converter \"{0}\" is missing.";

        /// <summary>
        /// Instantiates a new <see cref="ConverterNotFoundException"/>, signalling a missing converter
        /// </summary>
        /// <param name="converters">The known converters</param>
        /// <param name="converter">The missing converter</param>
        public ConverterNotFoundException(Dictionary<String, Func<String, object>> converters, String converter)
            : base(String.Format(MESSAGE, converter)) {
            Converters = converters;
            Converter = converter;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class QueryValueConverter : Attribute {
        public String Name { get; protected set; }

        public QueryValueConverter(String name) {
            Name = name;
        }

        public QueryValueConverter() {
            Name = null;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class QueryParameterAttribute: Attribute {
        public String Command { get; protected set; }
        public String Converter { get; protected set; }

        public QueryParameterAttribute(String command): this(command, null) {
        }

        public QueryParameterAttribute(String command, String converter) {
            Command = command;
            Converter = converter;
        }
    }

    public class QueryPropagationAttribute: Attribute {
    }
}

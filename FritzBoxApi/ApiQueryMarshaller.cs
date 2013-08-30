using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace FritzBoxApi {
    /// <summary>
    /// <p>A class for marshalling the query items of a declarative query definition into a real api query.</p><br/>
    /// <p>The <see cref="ApiQueryMarshaller"/> provides a convenient way of accessing values to queried via the api 
    /// in a declerative fashion. The query parameters of items can be declared using attributes like <see cref="QueryParameterAttribute"/>, 
    /// values can be converted automatically using converters defined by <see cref="QueryValueConverterAttribute"/>.</p><br/><br/>
    /// 
    /// <p>The attributes <see cref="QueryParameterAttribute"/> and <see cref="QueryPropagationAttribute"/> are used to declare members of an object as query items.</p><br/>
    /// <p>The attribute <see cref="QueryValueConverterAttribute"/> is used when defining a custom value converter. (e.g. for conversion of String to Integer)</p><br/>
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
    ///  * Either way you choose, the query object will be loaded with the items declared, or an exception will be thrown in case of failure.
    ///  *
    ///  * You can use the fields just as normal (further changes to the value won't propagate back to the api)
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

            CollectParameters(@object, list, new HashSet<Object>{ @object });

            return list;
        }

        /// <summary>
        /// Collects all suitable parameters of an object and its members marked with <see cref="QueryPropagationAttribute"/>
        /// </summary>
        /// <param name="object">The source object</param>
        /// <param name="qpprList">The result list</param>
        /// <param name="objectSet">The set of processed objects</param>
        protected static void CollectParameters(object @object, List<QueryParameterPropertyRecord> qpprList, ISet<object> objectSet) {
            // A) Collect all members
            var members = ExtractRelevantMembers(@object).ToList();

            // B.1) Find all converters
            var converterMembers = members.WhereDefined<QueryValueConverterAttribute>();
            
            // B.2) Build converter dictionary
            var converters = converterMembers.ToDictionary(m => {
                var n = m.GetCustomAttribute<QueryValueConverterAttribute>().Name;

                if(String.IsNullOrEmpty(n))
                    n = m.Name;

                return n;
            }, m => m.GetValue<Func<String, object>>(@object));

            // C.1) Filter query parameters properties
            var queryParameterProps = members.WhereDefined<QueryParameterAttribute>();

            // C.2) Build query list
            var d = queryParameterProps.Select(m => new QueryParameterPropertyRecord(@object, m.GetCustomAttribute<QueryParameterAttribute>(), m, converters)); 

            qpprList.AddRange(d);

            // D.1) Collect all sub-element parameters
            var subMems = members.WhereDefined<QueryPropagationAttribute>();

            // D.2) Process all sub-elements which haven't been already processed; prevents infinite recursion
            foreach(var sm in subMems.Where(m => !objectSet.Contains(m))) {
                objectSet.Add(@object);

                CollectParameters(sm.GetValue(@object), qpprList, objectSet);
            }
        }

        protected const BindingFlags RELEVANT_MEMBERS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Extracts all relevant members of an object.<br/>
        /// <p>All properties and fields are considered relevent if their binding matches the <see cref="BindingFlags"/> defined by <see cref="RELEVANT_MEMBERS"/>.</p>
        /// </summary>
        /// <param name="object">The object whose members should be extracted</param>
        /// <returns>An enumeration of members considered relevant</returns>
        protected static IEnumerable<MemberInfo> ExtractRelevantMembers(object @object) {
            return @object.GetType().GetProperties(RELEVANT_MEMBERS).Concat<MemberInfo>(@object.GetType().GetFields(RELEVANT_MEMBERS));
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
            /// The known converters that are visible to the <see cref="Member"/>
            /// </summary>
            public Dictionary<String, Func<String, object>> Converters;

            /// <summary>
            /// The Identity-Converter f(x) => x
            /// </summary>
            public static readonly Func<String, String> IDENTITY = (x => x);

            /// <summary>
            /// Instantiates a new <see cref="QueryParameterPropertyRecord"/>.
            /// </summary>
            /// <param name="object">The source object</param>
            /// <param name="attribute">The <see cref="QueryParameterAttribute"/> of the <paramref name="member"/></param>
            /// <param name="member">The attributed member</param>
            /// <param name="converters">The list of known converters visible to the <paramref name="member"/></param>
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

    #region Attributes

    /// <summary>
    /// An attribute used to define a custom converter function.
    /// <p>e.g. for conversion of String to Integer</p><br/>
    /// <p>The converter's name could either be specified explicitly by passing it as an argument 
    /// to the declaration of the attribute (See <see cref="QueryValueConverterAttribute(String)"/>), 
    /// or retrieved implicitly by the  <see cref="ApiQueryMarshaller"/> using the name of the 
    /// attributed member.</p>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class QueryValueConverterAttribute : Attribute {
        /// <summary>
        /// The converter's name, may be <c>null</c> when not declared explicitly
        /// </summary>
        public String Name { get; protected set; }

        /// <summary>
        /// Instantiates a new <see cref="QueryValueConverterAttribute"/> with explicitly defined name
        /// </summary>
        /// <param name="name"></param>
        public QueryValueConverterAttribute(String name) {
            Name = name;
        }

        /// <summary>
        /// Instantiates a new <see cref="QueryValueConverterAttribute"/> without a strongly defined name
        /// <p>The <see cref="ApiQueryMarshaller"/> will use attributed member's name.</p>
        /// </summary>
        public QueryValueConverterAttribute() {
            Name = null;
        }
    }

    /// <summary>
    /// An attribute used to declare the Api-<see cref="Command"/> used when querying a field.
    /// <p>A custom <see cref="Converter"/> can be referenced by it's name.</p>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class QueryParameterAttribute: Attribute {
        /// <summary>
        /// The declared api command
        /// </summary>
        public String Command { get; protected set; }

        /// <summary>
        /// The converter used, <c>null</c> if the default converter (<see cref="ApiQueryMarshaller.QueryParameterPropertyRecord.IDENTITY"/>) should be used
        /// </summary>
        public String Converter { get; protected set; }

        /// <summary>
        /// Instantiate a new <see cref="QueryParameterAttribute"/> using the given <paramref name="command"/> 
        /// and the default <see cref="ApiQueryMarshaller.QueryParameterPropertyRecord.IDENTITY"/> converter.
        /// </summary>
        /// <param name="command">The api command</param>
        public QueryParameterAttribute(String command): this(command, null) {
        }

        /// <summary>
        /// Instantiate a new <see cref="QueryParameterAttribute"/> using the given <paramref name="command"/> 
        /// and <paramref name="converter"/>-reference (by name).
        /// </summary>
        /// <param name="command">The api command</param>
        /// <param name="converter">The name of the converter to use</param>
        public QueryParameterAttribute(String command, String converter) {
            if(String.IsNullOrEmpty(command))
                throw new ArgumentException("The command must not be empty");

            Command = command;
            Converter = converter;
        }
    }

    /// <summary>
    /// An attribute used for signalling the propagation of the query-marshalling to a member object.
    /// <p>The <see cref="ApiQueryMarshaller"/> will respect the attributes of a member object when processing the parent ("this").</p>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class QueryPropagationAttribute: Attribute {
    }

    #endregion

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
        public ConverterNotFoundException(Dictionary<String, Func<String, object>> converters, String converter): base(String.Format(MESSAGE, converter)) {
            Converters = converters;
            Converter = converter;
        }
    }

    internal static class MemberInfoExtension {
        /// <summary>
        /// Filters the enumeration of <see cref="MemberInfo"/> objects based on whether any custom attributes of a specified type are applied to a member.<br/>
        /// <p>See also: <see cref="Attribute.IsDefined(System.Reflection.MemberInfo,System.Type,bool)"/></p>
        /// </summary>
        /// <typeparam name="T">The type of attribute to search for.</typeparam>
        /// <param name="members">The enumeration of members to inspect</param>
        /// <param name="inherit">Whether to inspect the ancestors of an element or not</param>
        /// <returns></returns>
        public static IEnumerable<MemberInfo> WhereDefined<T>(this IEnumerable<MemberInfo> members, bool inherit = true) where T : Attribute {
            return members.Where(m => Attribute.IsDefined(m, typeof(T), inherit));
        }
        
        /// <summary>
        /// Retrieves the <paramref name="member"/>'s value of the <paramref name="object"/>, and casting it to type <typeparamref name="T"/>.<br/>
        /// <p>Only Fields and Properties are supported.</p>
        /// </summary>
        /// <typeparam name="T">The type of the retrieved value</typeparam>
        /// <param name="member">The member</param>
        /// <param name="object">The object</param>
        /// <returns>The value</returns>
        public static T GetValue<T>(this MemberInfo member, object @object) {
            return (T)GetValue(member, @object);
        }

        /// <summary>
        /// Retrieves the <paramref name="member"/>'s value of the <paramref name="object"/>.<br/>
        /// <p>Only Fields and Properties are supported.</p>
        /// </summary>
        /// <param name="member">The member</param>
        /// <param name="object">The object</param>
        /// <returns>The value</returns>
        public static object GetValue(this MemberInfo member, object @object) {
            if(member is PropertyInfo)
                return ((member as PropertyInfo).GetValue(@object));

            if(member is FieldInfo)
                return ((member as FieldInfo).GetValue(@object));

            throw new Exception("MemberInfo is not supported");
        }
    }
}

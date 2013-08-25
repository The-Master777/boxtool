using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace FritzBoxApi {
    public class ConverterNotFoundException: Exception {
        public Dictionary<String, Func<String, object>> Converters { get; protected set; }
        public String Converter { get; set; }

        protected const String MESSAGE = "The converter \"{0}\" is missing.";

        public ConverterNotFoundException(Dictionary<String, Func<String, object>> converters, String converter): base(String.Format(MESSAGE, converter)) {
            Converters = converters;
            Converter = converter;
        }
    }

    public class ApiQueryMarshaller {
        public Session Api { get; protected set; }

        public ApiQueryMarshaller(Session api) {
            if(api == null)
                throw new ArgumentNullException("api");

            Api = api;
        }

        protected T GetMemberValue<T>(Object @object, MemberInfo member) {
            return (T)GetMemberValue(@object, member);
        }

        protected Object GetMemberValue(Object @object, MemberInfo member) {
            if(member is PropertyInfo) 
                return ((member as PropertyInfo).GetValue(@object));
            

            if(member is FieldInfo) 
                return ((member as FieldInfo).GetValue(@object));

            throw new Exception("MemberInfo is not supported");
        }

        protected IEnumerable<QueryParameterPropertyRecord> CollectParameters(Object @object) {
            var list = new List<QueryParameterPropertyRecord>();

            CollectParameters(@object, list);

            return list;
        }

        protected void CollectParameters(Object @object, List<QueryParameterPropertyRecord> qpprList) {
            const BindingFlags RELEVANT_MEMBERS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            // A) Collect all members
            var members = Enumerable.Concat<MemberInfo>(@object.GetType().GetProperties(RELEVANT_MEMBERS), @object.GetType().GetFields(RELEVANT_MEMBERS)).ToList();

            // B.1) Find all converters
            var converterMembers = members.Where(x => Attribute.IsDefined(x, typeof(QueryValueConverter), true));

            // B.2) Build converter dictionary
            var converters = converterMembers.ToDictionary(m => {
                var n = ((QueryValueConverter)Attribute.GetCustomAttribute(m, typeof(QueryValueConverter))).Name;

                if(String.IsNullOrEmpty(n))
                    n = m.Name;

                return n;
            } , m => GetMemberValue<Func<String, object>>(@object, m));

            // C.1) Filter query parameters properties
            var queryParameterProps = members.Where(x => Attribute.IsDefined(x, typeof(QueryParameterAttribute), true));

            // C.2) Build query list
            var d = queryParameterProps.Select(m => new QueryParameterPropertyRecord{ Object = @object, Attribute = (QueryParameterAttribute)Attribute.GetCustomAttribute(m, typeof(QueryParameterAttribute)), Member = m, Converters = converters});

            qpprList.AddRange(d);

            // D) Collect all sub-element parameters
            var subMems = members.Where(x => Attribute.IsDefined(x, typeof(QueryPropagationAttribute), true));

            foreach(var sm in subMems) {
                CollectParameters(GetMemberValue(@object, sm), qpprList);
            }
        }

        protected class QueryParameterPropertyRecord {
            public Object Object { get; set; }
            public QueryParameterAttribute Attribute { get; set; }
            public MemberInfo Member { get; set; }
            public Dictionary<String, Func<String, object>> Converters;

            public static readonly Func<String, String> IDENTITY = (t => t); 

            public Func<String, object> Converter {
                get {
                    if(Converters == null || String.IsNullOrEmpty(Attribute.Converter))
                        return IDENTITY;
                    
                    if(!Converters.ContainsKey(Attribute.Converter))
                        throw new ConverterNotFoundException(Converters, Attribute.Converter);

                    return Converters[Attribute.Converter];
                }
            } 

            public object Convert(String input) {
                return Converter(input);
            }
        }

        public async Task QueryAsync(Object @object, CancellationToken ct, IProgress<Tuple<int, int>> progress) {
            var d = CollectParameters(@object).ToList();

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

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class QueryValueConverter: Attribute {
        public String Name { get; protected set; }

        public QueryValueConverter(String name) {
            Name = name;
        }

        public QueryValueConverter() {
            Name = null;
        }
    }

    public class QueryPropagationAttribute: Attribute {
    }
}

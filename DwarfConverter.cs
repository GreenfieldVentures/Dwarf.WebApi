using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Dwarf.Extensions;
using Dwarf.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dwarf.WebApi
{
    /// <summary>
    /// JsonConverter to handle the IDwarfs
    /// </summary>
    public class DwarfConverter : JsonConverter
    {
        /// <summary>
        /// See base
        /// </summary>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("DwarfConverter should only be used while deserializing.");
        }

        /// <summary>
        /// See base
        /// </summary>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var jObject = JObject.Load(reader);
            
            object obj = null;

            if (jObject.Property("id") != null)
            {
                var id = jObject.Property("id").Value.ToObject<Guid?>();

                if (id.HasValue)
                    obj = DwarfHelper.Load(objectType, id.Value);

                if (obj == null)
                {
                    if (HttpContext.Current.Items.Contains(id.Value.ToString()))
                        obj = HttpContext.Current.Items[id.Value.ToString()];
                    else
                    {
                        obj = DwarfHelper.CreateInstance(objectType);
                        ((IDwarf) obj).Id = id;
                        HttpContext.Current.Items[id.Value.ToString()] = obj;
                    }
                }
            }
            else
                obj = DwarfHelper.CreateInstance(objectType);

            if (obj == null)
                throw new JsonSerializationException("No object created.");

            var writer = new StringWriter();
            serializer.Serialize(writer, jObject);

            using (var newReader = new JsonTextReader(new StringReader(writer.ToString())))
            {
                newReader.Culture = reader.Culture;
                newReader.DateParseHandling = reader.DateParseHandling;
                newReader.DateTimeZoneHandling = reader.DateTimeZoneHandling;
                newReader.FloatParseHandling = reader.FloatParseHandling;
                serializer.Populate(newReader, obj);
            }

            return obj;
        }

        /// <summary>
        /// See base
        /// </summary>
        public override bool CanConvert(Type objectType)
        {
            return objectType.Implements<IDwarf>();
        }

        public override bool CanWrite
        {
            get { return false; }
        }
    }
}

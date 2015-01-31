using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using Evergreen.Dwarf.Extensions;
using Evergreen.Dwarf.Interfaces;
using Newtonsoft.Json;

namespace Evergreen.Dwarf.WebApi
{
    /// <summary>
    /// JsonConverter to handle the states of the DwarfList's items 
    /// </summary>
    public class DwarfListConverter : JsonConverter
    {
        /// <summary>
        /// See base
        /// </summary>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartArray();

            var list = (IDwarfList)value;

            foreach (var obj in list)
                serializer.Serialize(writer, obj);

            writer.WriteEndArray();
        }

        /// <summary>
        /// See base
        /// </summary>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            dynamic list = existingValue;
            list.Clear();

            var innerType = objectType.GetGenericArguments()[0];

            var token = reader.TokenType;

            while (token != JsonToken.EndArray)
            {
                reader.Read();
                token = reader.TokenType;

                if (token == JsonToken.StartObject)
                    list.Add(list.Cast(serializer.Deserialize(reader, innerType)));
            }

            return list;
        }

        /// <summary>
        /// See base
        /// </summary>
        public override bool CanConvert(Type objectType)
        {
            return objectType.Implements<IDwarfList>();
        }
    }       
}

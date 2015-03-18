using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Evergreen.Dwarf.Extensions;
using Evergreen.Dwarf.Interfaces;
using Newtonsoft.Json;

namespace Evergreen.Dwarf.WebApi
{
    public class JsonIdOnlyConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            string id = null;

            if (value != null)
            {
                if (value.GetType().Implements<IGem>())
                    id = ((IGem) value).Id.ToString();
                else if (value.GetType().Implements<IDwarf>())
                    id = ((IDwarf)value).Id.ToString();
                else
                    throw new Exception("Wrong type...");
            }

            writer.WriteValue(id);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var value = reader.Value;

            if (value == null)
                return null;

            var sValue = value.ToString();

            if (string.IsNullOrEmpty(sValue))
                return null;

            if (objectType.Implements<IGem>())
                return GemHelper.Load(objectType, sValue);

            return null;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType.Implements<IGem>();
        }

        public override bool CanWrite
        {
            get { return true; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Evergreen.Dwarf.Attributes;
using Evergreen.Dwarf.Extensions;
using Evergreen.Dwarf.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Evergreen.Dwarf.WebApi
{
    /// <summary>
    /// Extends CamelCasePropertyNamesContractResolver but re-orders the properties to support the built-in OneToMany and ManyToMany collections
    /// </summary>
    public class CustomContractResolver : CamelCasePropertyNamesContractResolver
    {
        private static readonly JsonIdOnlyConverter jsonIdOnlyConverter = new JsonIdOnlyConverter();

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            if (!type.Implements<IDwarf>())
                return base.CreateProperties(type, memberSerialization);

            var members = GetSerializableMembers(type);

            if (members == null)
                throw new JsonSerializationException("Null collection of seralizable members returned.");

            var properties = new JsonPropertyCollection(type);

            foreach (var member in members)
            {
                var property = CreateProperty(member, memberSerialization);
                
                if (member.GetCustomAttributes<IgnoreDataMemberAttribute>().Any())
                    continue;

                if (member.GetCustomAttributes<JsonIdOnlyAttribute>().Any())
                {
                    property.PropertyName += "Id";
                    property.Converter = jsonIdOnlyConverter;
                }

                if (property != null)
                    properties.AddProperty(property);
            }

            var orderedProperties = properties.OrderBy(p => p.Order ?? -1).ToList();
            orderedProperties.Move(orderedProperties.FirstOrDefault(x => x.PropertyName == "id"), 0);
            orderedProperties.Move(orderedProperties.FirstOrDefault(x => x.PropertyName == "isSaved"), 1);

            return orderedProperties;
        }
    }
}

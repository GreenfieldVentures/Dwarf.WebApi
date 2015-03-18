using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Web.Http.Metadata;
using System.Web.Http.Metadata.Providers;
using Evergreen.Dwarf.Interfaces;

namespace Evergreen.Dwarf.WebApi
{
    /// <summary>
    /// Helper for hooking up Dwarf to WebApi
    /// </summary>
    public class DwarfWebApi
    {
        /// <summary>
        /// Registers the necessary.
        /// </summary>
        public static HttpConfiguration Register(HttpConfiguration config)
        {
            config.Formatters.JsonFormatter.SerializerSettings.Converters.Add(new DwarfListConverter());
            config.Formatters.JsonFormatter.SerializerSettings.Converters.Add(new DwarfConverter());
            config.Formatters.JsonFormatter.SerializerSettings.Converters.Add(new JsonIdOnlyConverter());
            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver = new CustomContractResolver();
            config.Services.Replace(typeof(System.Web.Http.Metadata.ModelMetadataProvider), new DataAnnotationsModelMetadataProviderEx());

            return config;
        }
    }

    public class DataAnnotationsModelMetadataProviderEx : AssociatedMetadataProviderEx<CachedDataAnnotationsModelMetadataEx>
    {
        protected override CachedDataAnnotationsModelMetadataEx CreateMetadataPrototype(IEnumerable<Attribute> attributes, Type containerType, Type modelType, string propertyName)
        {
            return new CachedDataAnnotationsModelMetadataEx(this, containerType, modelType, propertyName, attributes);
        }

        protected override CachedDataAnnotationsModelMetadataEx CreateMetadataFromPrototype(CachedDataAnnotationsModelMetadataEx prototype, Func<object> modelAccessor)
        {
            return new CachedDataAnnotationsModelMetadataEx(prototype, modelAccessor);
        }
    }

    public abstract class AssociatedMetadataProviderEx<TModelMetadata> : ModelMetadataProvider where TModelMetadata : ModelMetadata
    {
        private ConcurrentDictionary<Type, TypeInformation> _typeInfoCache = new ConcurrentDictionary<Type, TypeInformation>();

        public sealed override IEnumerable<ModelMetadata> GetMetadataForProperties(object container, Type containerType)
        {
            if (containerType == null)
            {
                throw new ArgumentNullException("containerType");
            }

            return GetMetadataForPropertiesImpl(container, containerType);
        }

        private IEnumerable<ModelMetadata> GetMetadataForPropertiesImpl(object container, Type containerType)
        {
            TypeInformation typeInfo = GetTypeInformation(containerType);
            foreach (KeyValuePair<string, PropertyInformation> kvp in typeInfo.Properties)
            {
                PropertyInformation propertyInfo = kvp.Value;
                Func<object> modelAccessor = null;
                if (container != null)
                {
                    Func<object, object> propertyGetter = propertyInfo.ValueAccessor;
                    modelAccessor = () => propertyGetter(container);
                }
                yield return CreateMetadataFromPrototype(propertyInfo.Prototype, modelAccessor);
            }
        }

        public sealed override ModelMetadata GetMetadataForProperty(Func<object> modelAccessor, Type containerType, string propertyName)
        {
            if (containerType == null)
            {
                throw new ArgumentNullException("containerType");
            }
            if (String.IsNullOrEmpty(propertyName))
            {
                throw new ArgumentNullException("propertyName");
            }

            TypeInformation typeInfo = GetTypeInformation(containerType);
            PropertyInformation propertyInfo;
            if (!typeInfo.Properties.TryGetValue(propertyName, out propertyInfo))
            {
                throw new ArgumentNullException("propertyName", propertyName);
            }

            return CreateMetadataFromPrototype(propertyInfo.Prototype, modelAccessor);
        }

        public sealed override ModelMetadata GetMetadataForType(Func<object> modelAccessor, Type modelType)
        {
            if (modelType == null)
            {
                throw new ArgumentNullException("modelType");
            }

            TModelMetadata prototype = GetTypeInformation(modelType).Prototype;
            return CreateMetadataFromPrototype(prototype, modelAccessor);
        }

        // Override for creating the prototype metadata (without the accessor)
        protected abstract TModelMetadata CreateMetadataPrototype(IEnumerable<Attribute> attributes, Type containerType, Type modelType, string propertyName);

        // Override for applying the prototype + modelAccess to yield the final metadata
        protected abstract TModelMetadata CreateMetadataFromPrototype(TModelMetadata prototype, Func<object> modelAccessor);

        private TypeInformation GetTypeInformation(Type type)
        {
            // This retrieval is implemented as a TryGetValue/TryAdd instead of a GetOrAdd to avoid the performance cost of creating instance delegates
            TypeInformation typeInfo;
            if (!_typeInfoCache.TryGetValue(type, out typeInfo))
            {
                typeInfo = CreateTypeInformation(type);
                _typeInfoCache.TryAdd(type, typeInfo);
            }
            return typeInfo;
        }

        private TypeInformation CreateTypeInformation(Type type)
        {
            TypeInformation info = new TypeInformation();
            ICustomTypeDescriptor typeDescriptor = new AssociatedMetadataTypeTypeDescriptionProvider(type).GetTypeDescriptor(type);
            info.TypeDescriptor = typeDescriptor;
            info.Prototype = CreateMetadataPrototype(AsAttributes(typeDescriptor.GetAttributes()), containerType: null, modelType: type, propertyName: null);

            Dictionary<string, PropertyInformation> properties = new Dictionary<string, PropertyInformation>();
            foreach (PropertyDescriptor property in typeDescriptor.GetProperties())
            {
                // Avoid re-generating a property descriptor if one has already been generated for the property name
                if (!properties.ContainsKey(property.Name))
                {
                    if (property.Attributes.OfType<IUnvalidatable>().Any())
                        continue;

                    properties.Add(property.Name, CreatePropertyInformation(type, property));
                }
            }
            info.Properties = properties;

            return info;
        }

        private PropertyInformation CreatePropertyInformation(Type containerType, PropertyDescriptor property)
        {
            PropertyInformation info = new PropertyInformation();
            info.ValueAccessor = CreatePropertyValueAccessor(property);
            info.Prototype = CreateMetadataPrototype(AsAttributes(property.Attributes), containerType, property.PropertyType, property.Name);
            return info;
        }

        // Optimization: yield provides much better performance than the LINQ .Cast<Attribute>() in this case
        private static IEnumerable<Attribute> AsAttributes(IEnumerable attributes)
        {
            foreach (object attribute in attributes)
            {
                yield return attribute as Attribute;
            }
        }

        private static Func<object, object> CreatePropertyValueAccessor(PropertyDescriptor property)
        {
            Type declaringType = property.ComponentType;
            if (declaringType.IsVisible)
            {
                string propertyName = property.Name;
                PropertyInfo propertyInfo = declaringType.GetProperty(propertyName, property.PropertyType);

                if (propertyInfo != null && propertyInfo.CanRead)
                {
                    MethodInfo getMethodInfo = propertyInfo.GetGetMethod();
                    if (getMethodInfo != null)
                    {
                        return CreateDynamicValueAccessor(getMethodInfo, declaringType, propertyName);
                    }
                }
            }

            // If either the type isn't public or we can't find a public getter, use the slow Reflection path
            return container => property.GetValue(container);
        }

        // Uses Lightweight Code Gen to generate a tiny delegate that gets the property value
        // This is an optimization to avoid having to go through the much slower System.Reflection APIs
        // e.g. generates (object o) => (Person)o.Id
        private static Func<object, object> CreateDynamicValueAccessor(MethodInfo getMethodInfo, Type declaringType, string propertyName)
        {
            Contract.Assert(getMethodInfo != null && getMethodInfo.IsPublic && !getMethodInfo.IsStatic);

            Type propertyType = getMethodInfo.ReturnType;
            DynamicMethod dynamicMethod = new DynamicMethod("Get" + propertyName + "From" + declaringType.Name, typeof(object), new Type[] { typeof(object) });
            ILGenerator ilg = dynamicMethod.GetILGenerator();

            // Load the container onto the stack, convert from object => declaring type for the property
            ilg.Emit(OpCodes.Ldarg_0);
            if (declaringType.IsValueType)
            {
                ilg.Emit(OpCodes.Unbox, declaringType);
            }
            else
            {
                ilg.Emit(OpCodes.Castclass, declaringType);
            }

            // if declaring type is value type, we use Call : structs don't have inheritance
            // if get method is sealed or isn't virtual, we use Call : it can't be overridden
            if (declaringType.IsValueType || !getMethodInfo.IsVirtual || getMethodInfo.IsFinal)
            {
                ilg.Emit(OpCodes.Call, getMethodInfo);
            }
            else
            {
                ilg.Emit(OpCodes.Callvirt, getMethodInfo);
            }

            // Box if the property type is a value type, so it can be returned as an object
            if (propertyType.IsValueType)
            {
                ilg.Emit(OpCodes.Box, propertyType);
            }

            // Return property value
            ilg.Emit(OpCodes.Ret);

            return (Func<object, object>)dynamicMethod.CreateDelegate(typeof(Func<object, object>));
        }

        private class TypeInformation
        {
            public ICustomTypeDescriptor TypeDescriptor { get; set; }
            public TModelMetadata Prototype { get; set; }
            public Dictionary<string, PropertyInformation> Properties { get; set; }
        }

        private class PropertyInformation
        {
            public Func<object, object> ValueAccessor { get; set; }
            public TModelMetadata Prototype { get; set; }
        }
    }

    public class CachedDataAnnotationsModelMetadataEx : CachedModelMetadataEx<CachedDataAnnotationsMetadataAttributes>
    {
        public CachedDataAnnotationsModelMetadataEx(CachedDataAnnotationsModelMetadataEx prototype, Func<object> modelAccessor)
            : base(prototype, modelAccessor)
        {
        }

        public CachedDataAnnotationsModelMetadataEx(DataAnnotationsModelMetadataProviderEx provider, Type containerType, Type modelType, string propertyName, IEnumerable<Attribute> attributes)
            : base(provider, containerType, modelType, propertyName, new CachedDataAnnotationsMetadataAttributes(attributes))
        {
        }

        protected override bool ComputeConvertEmptyStringToNull()
        {
            return PrototypeCache.DisplayFormat != null
                       ? PrototypeCache.DisplayFormat.ConvertEmptyStringToNull
                       : base.ComputeConvertEmptyStringToNull();
        }

        protected override string ComputeDescription()
        {
            return PrototypeCache.Display != null
                       ? PrototypeCache.Display.GetDescription()
                       : base.ComputeDescription();
        }

        protected override bool ComputeIsReadOnly()
        {
            if (PrototypeCache.Editable != null)
            {
                return !PrototypeCache.Editable.AllowEdit;
            }

            if (PrototypeCache.ReadOnly != null)
            {
                return PrototypeCache.ReadOnly.IsReadOnly;
            }

            return base.ComputeIsReadOnly();
        }

        public override string GetDisplayName()
        {
            // DisplayName could be provided by either the DisplayAttribute, or DisplayNameAttribute. If neither of
            // those supply a name, then we fall back to the property name (in base.GetDisplayName()).
            // 
            // DisplayName has lower precedence than Display.Name, for consistency with MVC.

            // DisplayAttribute doesn't require you to set a name, so this could be null. 
            if (PrototypeCache.Display != null)
            {
                var name = PrototypeCache.Display.GetName();

                if (name != null)
                    return name;
            }

            // It's also possible for DisplayNameAttribute to be used without setting a name. If a user does that, then DisplayName will
            // return the empty string - but for consistency with MVC we allow it. We do fallback to the property name in the (unlikely)
            // scenario that the user sets null as the DisplayName, again, for consistency with MVC.
            if (PrototypeCache.DisplayName != null)
            {
                string name = PrototypeCache.DisplayName.DisplayName;
                if (name != null)
                {
                    return name;
                }
            }

            // If neither attribute specifies a name, we'll fall back to the property name.
            return base.GetDisplayName();
        }
    }

    public abstract class CachedModelMetadataEx<TPrototypeCache> : ModelMetadata
    {
        private bool _convertEmptyStringToNull;
        private string _description;
        private bool _isReadOnly;
        private bool _isComplexType;

        private bool _convertEmptyStringToNullComputed;
        private bool _descriptionComputed;
        private bool _isReadOnlyComputed;
        private bool _isComplexTypeComputed;

        private static EfficientTypePropertyKey<Type, string> CreateCacheKey(Type containerType, Type modelType, string propertyName)
        {
            // If metadata is for a property then containerType != null && propertyName != null
            // If metadata is for a type then containerType == null && propertyName == null, so we have to use modelType for the cache key.
            return new EfficientTypePropertyKey<Type, string>(containerType ?? modelType, propertyName);
        }

        // Constructor for creating real instances of the metadata class based on a prototype
        protected CachedModelMetadataEx(CachedModelMetadataEx<TPrototypeCache> prototype, Func<object> modelAccessor)
            : base(prototype.Provider, prototype.ContainerType, modelAccessor, prototype.ModelType, prototype.PropertyName)
        {
            PrototypeCache = prototype.PrototypeCache;

            _isComplexType = prototype.IsComplexType;
            _isComplexTypeComputed = true;
        }

        // Constructor for creating the prototype instances of the metadata class
        protected CachedModelMetadataEx(DataAnnotationsModelMetadataProviderEx provider, Type containerType, Type modelType, string propertyName, TPrototypeCache prototypeCache)
            : base(provider, containerType, null, modelType, propertyName)
        {
            PrototypeCache = prototypeCache;
        }

        public sealed override bool ConvertEmptyStringToNull
        {
            get
            {
                if (!_convertEmptyStringToNullComputed)
                {
                    _convertEmptyStringToNull = ComputeConvertEmptyStringToNull();
                    _convertEmptyStringToNullComputed = true;
                }
                return _convertEmptyStringToNull;
            }
            set
            {
                _convertEmptyStringToNull = value;
                _convertEmptyStringToNullComputed = true;
            }
        }

        public sealed override string Description
        {
            get
            {
                if (!_descriptionComputed)
                {
                    _description = ComputeDescription();
                    _descriptionComputed = true;
                }
                return _description;
            }
            set
            {
                _description = value;
                _descriptionComputed = true;
            }
        }

        public sealed override bool IsReadOnly
        {
            get
            {
                if (!_isReadOnlyComputed)
                {
                    _isReadOnly = ComputeIsReadOnly();
                    _isReadOnlyComputed = true;
                }
                return _isReadOnly;
            }
            set
            {
                _isReadOnly = value;
                _isReadOnlyComputed = true;
            }
        }

        public sealed override bool IsComplexType
        {
            get
            {
                if (!_isComplexTypeComputed)
                {
                    _isComplexType = ComputeIsComplexType();
                    _isComplexTypeComputed = true;
                }
                return _isComplexType;
            }
        }

        protected TPrototypeCache PrototypeCache { get; set; }

        protected virtual bool ComputeConvertEmptyStringToNull()
        {
            return base.ConvertEmptyStringToNull;
        }

        protected virtual string ComputeDescription()
        {
            return base.Description;
        }

        protected virtual bool ComputeIsReadOnly()
        {
            return base.IsReadOnly;
        }

        protected virtual bool ComputeIsComplexType()
        {
            return base.IsComplexType;
        }
    }

    internal class EfficientTypePropertyKey<T1, T2> : Tuple<T1, T2>
    {
        private int _hashCode;

        public EfficientTypePropertyKey(T1 item1, T2 item2)
            : base(item1, item2)
        {
            _hashCode = base.GetHashCode();
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }
    }
}

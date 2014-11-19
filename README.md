Dwarf.WebApi
============
Dwarf.WebApi is an extension to Dwarf enabling Dwarf objects in controllers. Dwarf.WebApi retains the promose of only one copy per object, even when deserializing 'internal' objects from 'external' sources

To use, simply run your HttpConfiguration through the following piece of code
```csharp
DwarfWebApi.Register(config);
```

In a new WebApi project a good place would be in the static Register method in the WebApiConfig class

###The optional DwarfController
When creating controllers for your model you can enherit from DwarfController<T> instead of ApiController. This gives you Get(), Get(Guid id), Post(T t), Put(T t) and Delete(Guid id) out of the box. Post and Put validates the model prior to saving. These methods are overridable.

Example of a full featured controller
```csharp
    public class PersonController : DwarfController<Person>
    {
    }
```


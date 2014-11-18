Dwarf.WebApi
============
Dwarf.WebApi is an extension to Dwarf enabling Dwarf objects in controllers. Dwarf.WebApi retains he promose of only one copy per object.

To use, simply run your HttpConfiguration through the following piece of code
```csharp
DwarfWebApi.Register(config);
```

In a new WebApi project a good place would be in the static Register method in the WebApiConfig class

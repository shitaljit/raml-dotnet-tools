using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Raml.Common;
using Raml.Parser.Expressions;
using Raml.Tools.Pluralization;

namespace Raml.Tools
{
    public abstract class GeneratorServiceBase
    {
        private readonly ObjectParser objectParser = new ObjectParser();
        
        protected readonly string[] suffixes = { "A", "B", "C", "D", "E", "F", "G" };

        protected readonly UriParametersGenerator uriParametersGenerator = new UriParametersGenerator();
        protected readonly SchemaParameterParser schemaParameterParser = new SchemaParameterParser(new EnglishPluralizationService());
        protected IDictionary<string, ApiObject> schemaObjects = new Dictionary<string, ApiObject>();
        protected IDictionary<string, ApiObject> schemaRequestObjects = new Dictionary<string, ApiObject>();
        protected IDictionary<string, ApiObject> schemaResponseObjects = new Dictionary<string, ApiObject>();
        protected IDictionary<string, string> linkKeysWithObjectNames = new Dictionary<string, string>();

        protected readonly ApiObjectsCleaner apiObjectsCleaner;        

		protected IDictionary<string, string> warnings;
	    protected IDictionary<string, ApiEnum> enums;
		protected readonly RamlDocument raml;
	    private readonly string targetNamespace;
	    protected ICollection<string> classesNames;
		protected IDictionary<string, ApiObject> uriParameterObjects;
		public const string RequestContentSuffix = "RequestContent";
		public const string ResponseContentSuffix = "ResponseContent";

        public RamlDocument ParsedContent { get { return raml; } }

        public IEnumerable<ApiEnum> Enums { get { return enums.Values; } }

		protected GeneratorServiceBase(RamlDocument raml, string targetNamespace)
		{
			this.raml = raml;
		    this.targetNamespace = targetNamespace;
		    apiObjectsCleaner = new ApiObjectsCleaner(schemaRequestObjects, schemaResponseObjects, schemaObjects);
		}


        protected string GetUrl(string url, string relativeUrl)
        {
            var res = !string.IsNullOrWhiteSpace(url) ? url.Substring(1) : string.Empty;
            return string.IsNullOrWhiteSpace(res) ? relativeUrl.Substring(1) : url + relativeUrl;
        }

        protected static string CalculateClassKey(string url)
        {
            return url.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        protected static void GetInheritedUriParams(IDictionary<string, Parameter> parentUriParameters, Resource resource)
        {
            foreach (var uriParams in resource.UriParameters)
            {
                if (!parentUriParameters.ContainsKey(uriParams.Key))
                    parentUriParameters.Add(uriParams);
            }
        }

        private void ParseResourceTypesRequests()
        {
            foreach (var resourceType in raml.ResourceTypes)
            {
                foreach (var type in resourceType)
                {
                    if (type.Value.Post != null) ParseRequests(type, type.Value.Post);
                    if (type.Value.Put != null) ParseRequests(type, type.Value.Put);
                    if (type.Value.Delete != null) ParseRequests(type, type.Value.Delete);
                    if (type.Value.Patch != null) ParseRequests(type, type.Value.Patch);
                    if (type.Value.Options != null) ParseRequests(type, type.Value.Options);
                }
            }
        }

        private void ParseRequests(KeyValuePair<string, ResourceType> type, Verb verb)
        {
            if (verb.Body == null)
                return;

            var name = Enum.GetName(typeof(VerbType), verb.Type);
            if (name == null) 
                return;

            var key = type.Key + "-" + name.ToLower() + RequestContentSuffix;
            var obj = objectParser.ParseObject(key, verb.Body.Schema, schemaRequestObjects, warnings, enums, schemaResponseObjects, schemaObjects, targetNamespace);

            AddObjectToObjectCollectionOrLink(obj, key, schemaRequestObjects, schemaObjects);
        }

        private void ParseTraitsResponses()
        {
            foreach (var trait in raml.Traits)
            {
                foreach (var method in trait)
                {
                    foreach (var response in method.Value.Responses.Where(r => r.Body != null))
                    {
                        foreach (var mimeType in response.Body)
                        {
                            var key = mimeType.Key + " " + mimeType.Value.Type + ResponseContentSuffix;
                            var obj = objectParser.ParseObject(key, mimeType.Value.Schema, schemaResponseObjects, warnings, enums, schemaRequestObjects, schemaObjects, targetNamespace);

                            AddObjectToObjectCollectionOrLink(obj, key, schemaResponseObjects, schemaObjects);
                        }
                    }
                }
            }
        }


        private void ParseResourceTypesResponses()
        {
            foreach (var resourceType in raml.ResourceTypes)
            {
                foreach (var type in resourceType)
                {
                    if (type.Value.Get != null) ParseResponses(type, type.Value.Get);
                    if (type.Value.Post != null) ParseResponses(type, type.Value.Post);
                    if (type.Value.Put != null) ParseResponses(type, type.Value.Put);
                    if (type.Value.Delete != null) ParseResponses(type, type.Value.Delete);
                    if (type.Value.Patch != null) ParseResponses(type, type.Value.Patch);
                    if (type.Value.Options != null) ParseResponses(type, type.Value.Options);
                }
            }
        }

        private void ParseResponses(KeyValuePair<string, ResourceType> type, Verb verb)
        {
            if (verb.Responses == null)
                return;

            foreach (var response in verb.Responses.Where(response => response != null))
            {
                var name = Enum.GetName(typeof(VerbType), verb.Type);
                if (name == null) 
                    continue;

                var key = type.Key + "-" + name.ToLower() + ParserHelpers.GetStatusCode(response.Code) + ResponseContentSuffix;

                if (response.Body == null || !response.Body.Any(b => b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Schema)))
                    continue;

                var mimeType = GeneratorServiceHelper.GetMimeType(response);

                var obj = objectParser.ParseObject(key, mimeType.Schema, schemaResponseObjects, warnings, enums, schemaRequestObjects, schemaObjects, targetNamespace);

                AddObjectToObjectCollectionOrLink(obj, key, schemaResponseObjects, schemaObjects);
            }
        }



        private void ParseResourceRequestsRecursively(IEnumerable<Resource> resources, string fullUrl)
        {
            foreach (var resource in resources)
            {
                if (resource.Methods != null)
                {
                    foreach (var method in resource.Methods.Where(m => m.Body != null && m.Body.Any()))
                    {
                        foreach (var kv in method.Body.Where(b => b.Value.Schema != null))
                        {
                            var key = GeneratorServiceHelper.GetKeyForResource(method, resource, fullUrl) + RequestContentSuffix;
                            if (schemaRequestObjects.ContainsKey(key)) 
                                continue;

                            var obj = objectParser.ParseObject(key, kv.Value.Schema, schemaRequestObjects, warnings, enums, schemaResponseObjects, schemaObjects, targetNamespace);

                            AddObjectToObjectCollectionOrLink(obj, key, schemaRequestObjects, schemaObjects);                                
                        }
                    }
                }
                if (resource.Resources != null)
                    ParseResourceRequestsRecursively(resource.Resources, fullUrl + resource.RelativeUri);
            }
        }

        protected IDictionary<string, ApiObject> GetRequestObjects()
        {
            ParseResourcesRequests();
            ParseResourceTypesRequests();

            return schemaRequestObjects;
        }

        private void ParseResourcesRequests()
        {
            var resources = raml.Resources;
            ParseResourceRequestsRecursively(resources, "");
        }


        protected IDictionary<string, ApiObject> GetResponseObjects()
        {
            ParseResourceTypesResponses();
            ParseTraitsResponses();
            ParseResourcesResponses();

            return schemaResponseObjects;
        }

        private void ParseResourcesResponses()
        {
            var resources = raml.Resources;
            ParseResourceResponsesRecursively(resources, "");
        }

        private void ParseResourceResponsesRecursively(IEnumerable<Resource> resources, string fullUrl)
        {
            foreach (var resource in resources)
            {
                if (resource.Methods != null)
                {
                    foreach (var method in resource.Methods.Where(m => m.Responses != null && m.Responses.Any()))
                    {
                        foreach (var response in method.Responses.Where(r => r.Body != null && r.Body.Any()))
                        {
                            foreach (var kv in response.Body.Where(b => b.Value.Schema != null))
                            {
                                var key = GeneratorServiceHelper.GetKeyForResource(method, resource, fullUrl) + ParserHelpers.GetStatusCode(response.Code) + ResponseContentSuffix;
                                if (schemaResponseObjects.ContainsKey(key)) continue;

                                var obj = objectParser.ParseObject(key, kv.Value.Schema, schemaResponseObjects, warnings, enums, schemaRequestObjects, schemaObjects, targetNamespace);

                                AddObjectToObjectCollectionOrLink(obj, key, schemaResponseObjects, schemaObjects);
                            }
                        }
                    }
                }
                if (resource.Resources != null)
                    ParseResourceResponsesRecursively(resource.Resources, fullUrl + resource.RelativeUri);
            }
        }

        private void AddObjectToObjectCollectionOrLink(ApiObject obj, string key, IDictionary<string, ApiObject> objects, IDictionary<string, ApiObject> otherObjects)
        {
            if (obj == null || !obj.Properties.Any())
                return;

            if (schemaObjects.All(o => o.Value.Name != obj.Name) && objects.All(o => o.Value.Name != obj.Name) && otherObjects.All(o => o.Value.Name != obj.Name))
            {
                objects.Add(key, obj);
            }
            else
            {
                if (UniquenessHelper.HasSameProperties(obj, objects, key, otherObjects, schemaObjects))
                {
                    if (!linkKeysWithObjectNames.ContainsKey(key))
                        linkKeysWithObjectNames.Add(key, obj.Name);
                }
                else if(!objects.ContainsKey(key))
                {
                    obj.Name = UniquenessHelper.GetUniqueName(objects, obj.Name, schemaObjects, schemaObjects);
                    objects.Add(key, obj);
                }
            }
        }

        protected void CleanProperties(IDictionary<string, ApiObject> apiObjects)
        {
            var keys = apiObjects.Keys.ToList();
            var apiObjectsCount = keys.Count - 1;
            for (var i = apiObjectsCount; i >= 0; i--)
            {
                var apiObject = apiObjects[keys[i]];
                var count = apiObject.Properties.Count;
                for (var index = count - 1; index >= 0; index--)
                {
                    var prop = apiObject.Properties[index];
                    var type = prop.Type;
                    if (!string.IsNullOrWhiteSpace(type) && IsCollectionType(type))
                        type = CollectionTypeHelper.GetBaseType(type);

                    if (!NetTypeMapper.IsPrimitiveType(type) && schemaResponseObjects.All(o => o.Value.Name != type) 
                        && schemaRequestObjects.All(o => o.Value.Name != type)
                        && enums.All(e => e.Value.Name != type)
                        && schemaObjects.All(o => o.Value.Name != type))
                        apiObject.Properties.Remove(prop);
                }
            }
        }

        private bool IsCollectionType(string type)
        {
            return type.EndsWith(">") && type.StartsWith(CollectionTypeHelper.CollectionType);
        }

        protected void ParseSchemas()
        {
            foreach (var schema in raml.Schemas)
            {
                foreach (var kv in schema)
                {
                    var key = kv.Key;

                    var obj = objectParser.ParseObject(key, kv.Value, schemaObjects, warnings, enums, new Dictionary<string, ApiObject>(), new Dictionary<string, ApiObject>(), targetNamespace);

                    if (schemaObjects.ContainsKey(key))
                    {
                        if (UniquenessHelper.HasSameProperties(schemaObjects[key], schemaObjects, key,
                            new Dictionary<string, ApiObject>(), new Dictionary<string, ApiObject>()))
                            continue;

                        var apiObject = schemaObjects[key];
                        var oldName = apiObject.Name;
                        apiObject.Name = UniquenessHelper.GetUniqueName(schemaObjects, apiObject.Name, new Dictionary<string, ApiObject>(), new Dictionary<string, ApiObject>());
                        key = UniquenessHelper.GetUniqueKey(schemaObjects, key, new Dictionary<string, ApiObject>());
                        schemaObjects.Add(key, apiObject);
                        schemaObjects.Remove(schemaObjects.First(o => o.Key == key));
                        foreach (var apiObj in schemaObjects)
                        {
                            foreach (var prop in apiObj.Value.Properties)
                            {
                                if (prop.Type == oldName)
                                    prop.Type = apiObject.Name;
                            }
                        }
                        
                        
                    }
                    schemaObjects.Add(key, obj);
                }
            }
        }

        protected string GetUniqueObjectName(Resource resource, Resource parent)
        {
            string objectName;

            if (resource.RelativeUri.StartsWith("/{") && resource.RelativeUri.EndsWith("}"))
            {
                objectName = NetNamingMapper.Capitalize(GetObjectNameForParameter(resource));
            }
            else
            {
                objectName = NetNamingMapper.GetObjectName(resource.RelativeUri);
                if (classesNames.Contains(objectName))
                    objectName = NetNamingMapper.Capitalize(GetObjectNameForParameter(resource));
            }

            if (string.IsNullOrWhiteSpace(objectName))
                throw new InvalidOperationException("object name is null for " + resource.RelativeUri);

            if (!classesNames.Contains(objectName))
                return objectName;

            if (parent == null || string.IsNullOrWhiteSpace(parent.RelativeUri))
                return GetUniqueObjectName(objectName);

            if (resource.RelativeUri.StartsWith("/{") && parent.RelativeUri.EndsWith("}"))
            {
                objectName = NetNamingMapper.Capitalize(GetObjectNameForParameter(parent)) + objectName;
            }
            else
            {
                objectName = NetNamingMapper.GetObjectName(parent.RelativeUri) + objectName;
                if (classesNames.Contains(objectName))
                    objectName = NetNamingMapper.Capitalize(GetObjectNameForParameter(parent));
            }

            if (string.IsNullOrWhiteSpace(objectName))
                throw new InvalidOperationException("object name is null for " + resource.RelativeUri);

            if (!classesNames.Contains(objectName))
                return objectName;

            return GetUniqueObjectName(objectName);
        }

        private string GetUniqueObjectName(string name)
        {
            for (var i = 0; i < 7; i++)
            {
                var unique = name + suffixes[i];
                if (!classesNames.Contains(unique))
                    return unique;
            }
            for (var i = 0; i < 100; i++)
            {
                var unique = name + i;
                if (!classesNames.Contains(unique))
                    return unique;
            }
            throw new InvalidOperationException("Could not find a unique name for object " + name);
        }

        private static string GetObjectNameForParameter(Resource resource)
        {
            var relativeUri = resource.RelativeUri.Replace("{mediaTypeExtension}", string.Empty);
            var objectNameForParameter = relativeUri.Substring(1).Replace("{", string.Empty).Replace("}", string.Empty);
            objectNameForParameter = NetNamingMapper.GetObjectName(objectNameForParameter);
            return objectNameForParameter;
        }

    }
}
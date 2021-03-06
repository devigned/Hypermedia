﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hypermedia.Json;
using Hypermedia.Metadata;
using JsonLite.Ast;

namespace Hypermedia.JsonApi
{
    public sealed class JsonApiSerializer
    {
        readonly IContractResolver _contractResolver;
        readonly IFieldNamingStrategy _fieldNamingStrategy;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="contractResolver">The resource contract resolver.</param>
        public JsonApiSerializer(IContractResolver contractResolver) : this(contractResolver, new DasherizedFieldNamingStrategy()) { }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="contractResolver">The resource contract resolver.</param>
        /// <param name="fieldNamingStrategy">The field naming strategy.</param>
        public JsonApiSerializer(IContractResolver contractResolver, IFieldNamingStrategy fieldNamingStrategy)
        {
            _contractResolver = contractResolver;
            _fieldNamingStrategy = fieldNamingStrategy;
        }

        /// <summary>
        /// Serialize a list of entities.
        /// </summary>
        /// <typeparam name="TEntity">The type of instance to serialize.</typeparam>
        /// <param name="entities">The list of entities to serialize.</param>
        /// <returns>The JSON object that represents the serialized entity.</returns>
        public JsonObject SerializeMany<TEntity>(IEnumerable<TEntity> entities)
        {
            return SerializeMany((IEnumerable)entities);
        }

        /// <summary>
        /// Serialize a list of entities.
        /// </summary>
        /// <param name="entities">The list of entities to serialize.</param>
        /// <returns>The JSON object that represents the serialized entity.</returns>
        public JsonObject SerializeMany(IEnumerable entities)
        {
            var serializer = new Serializer(_contractResolver, _fieldNamingStrategy);

            var members = new List<JsonMember>
            {
                new JsonMember("data", new JsonArray(serializer.Serialize(entities).ToList()))
            };

            var included = serializer.SerializeIncluded(entities).ToList();

            if (included.Count > 0)
            {
                members.Add(new JsonMember("included", new JsonArray(included)));
            }

            return new JsonObject(members);
        }

        /// <summary>
        /// Serialize the an entity.
        /// </summary>
        /// <typeparam name="TEntity">The type of instance to serialize.</typeparam>
        /// <param name="entity">The entity to serialize.</param>
        /// <returns>The JSON object that represents the serialized entity.</returns>
        public JsonObject SerializeEntity<TEntity>(TEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            return SerializeEntity((object)entity);
        }

        /// <summary>
        /// Serialize the an entity.
        /// </summary>
        /// <param name="entity">The entity to serialize.</param>
        /// <returns>The JSON object that represents the serialized entity.</returns>
        public JsonObject SerializeEntity(object entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var serializer = new Serializer(_contractResolver, _fieldNamingStrategy);

            var members = new List<JsonMember>
            {
                new JsonMember("jsonapi", new JsonObject(new JsonMember("version", new JsonString("1.0")))),
                new JsonMember("data", serializer.Serialize(new[] { entity }).First())
            };

            var included = serializer.SerializeIncluded(new[] { entity }).ToList();

            if (included.Count > 0)
            {
                members.Add(new JsonMember("included", new JsonArray(included)));
            }

            return new JsonObject(members);
        }

        /// <summary>
        /// Deserialize a collection of items.
        /// </summary>
        /// <param name="jsonObject">The top level object to deserialize.</param>
        /// <returns>The list of items that was deserialized.</returns>
        public IEnumerable<object> DeserializeMany(JsonObject jsonObject)
        {
            return DeserializeMany(jsonObject, new JsonApiEntityCache());
        }

        /// <summary>
        /// Deserialize a collection of items.
        /// </summary>
        /// <param name="jsonObject">The top level object to deserialize.</param>
        /// <param name="cache">The entity cache to use for resolving existing instances in the object graph.</param>
        /// <returns>The list of items that was deserialized.</returns>
        public IEnumerable<object> DeserializeMany(JsonObject jsonObject, IJsonApiEntityCache cache)
        {
            var deserializer = new Deserializer(jsonObject, _contractResolver, _fieldNamingStrategy, cache);

            return deserializer.DeserializeMany();
        }

        /// <summary>
        /// Deserialize a JSON object into a CLR type.
        /// </summary>
        /// <param name="jsonObject">The top level JSON object to deserialize into a CLR type.</param>
        /// <returns>The instance that was created.</returns>
        public object DeserializeEntity(JsonObject jsonObject)
        {
            return DeserializeEntity(jsonObject, new JsonApiEntityCache());
        }

        /// <summary>
        /// Deserialize a JSON object into a CLR type.
        /// </summary>
        /// <param name="jsonObject">The top level JSON object to deserialize into a CLR type.</param>
        /// <param name="cache">The entity cache to use for resolving existing instances in the object graph.</param>
        /// <returns>The instance that was created.</returns>
        public object DeserializeEntity(JsonObject jsonObject, IJsonApiEntityCache cache)
        {
            var deserializer = new Deserializer(jsonObject, _contractResolver, _fieldNamingStrategy, cache);

            return deserializer.DeserializeEntity();
        }

        /// <summary>
        /// Deserialize an object.
        /// </summary>
        /// <param name="type">The entity type of the object to deserialize.</param>
        /// <param name="jsonObject">The JSON object that represents the object to deserialize.</param>
        /// <param name="entity">The entity instance to deserialize the fields into.</param>
        internal void DeserializeEntity(IContract type, JsonObject jsonObject, object entity)
        {
            var deserializer = new Deserializer(jsonObject, new ContractResolver(type), _fieldNamingStrategy, new JsonApiEntityCache());
            
            deserializer.DeserializeEntity(type, jsonObject, entity);
        }

        #region Serializer

        class Serializer : IJsonConverter
        {
            readonly IContractResolver _contractResolver;
            readonly IJsonSerializer _jsonSerializer;
            readonly HashSet<JsonObject> _visited = new HashSet<JsonObject>(JsonApiEntityKeyEqualityComparer.Instance);

            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="contractResolver">The resource contract resolver.</param>
            /// <param name="fieldNamingStrategy">The field naming strategy.</param>
            internal Serializer(IContractResolver contractResolver, IFieldNamingStrategy fieldNamingStrategy)
            {
                _contractResolver = contractResolver;
                _jsonSerializer = new JsonSerializer(new JsonConverterFactory(), fieldNamingStrategy);
            }

            /// <summary>
            /// Serialize the list of entities.
            /// </summary>
            /// <param name="entities">The list of entities to serialize.</param>
            /// <returns>The list of entities that were serialzied.</returns>
            internal IEnumerable<JsonObject> Serialize(IEnumerable entities)
            {
                foreach (var entity in entities)
                {
                    IContract contract;
                    if (_contractResolver.TryResolve(entity.GetType(), out contract) == false)
                    {
                        throw new JsonApiException("Can not serialize an unknown resource type '{0}'.", entity.GetType());
                    }

                    var jsonObject = SerializeEntity(contract, entity);

                    if (HasVisited(jsonObject) == false)
                    {
                        Visit(jsonObject);

                        yield return jsonObject;
                    }
                }
            }

            /// <summary>
            /// Returns a value indicating whether the entity that the given JSON object represents has been visited.
            /// </summary>
            /// <param name="jsonObject">The JSON object that defines an entity.</param>
            /// <returns>true if the entity represented by the JSON object has been visitied, false if not.</returns>
            bool HasVisited(JsonObject jsonObject)
            {
                return _visited.Contains(jsonObject);
            }

            /// <summary>
            /// Marks the entity represented by the JSON object as having being visited.
            /// </summary>
            /// <param name="jsonObject">The JSON object that represents an entity.</param>
            void Visit(JsonObject jsonObject)
            {
                _visited.Add(jsonObject);
            }

            /// <summary>
            /// Serialize an entity.
            /// </summary>
            /// <param name="contract">The resource contract.</param>
            /// <param name="entity">The entity to serialize.</param>
            /// <returns>The JSON object that represents the serialized entity.</returns>
            JsonObject SerializeEntity(IContract contract, object entity)
            {
                var members = new List<JsonMember>(SerializeResourceKey(contract, entity));

                var fields = SerializeFields(contract, entity).Where(IsNotNull).ToList();
                if (fields.Any())
                {
                    members.Add(new JsonMember("attributes", new JsonObject(fields)));
                }

                var relationships = SerializeRelationships(contract, entity);
                if (relationships.Any())
                {
                    members.Add(new JsonMember("relationships", new JsonObject(relationships)));
                }

                return new JsonObject(members);
            }

            /// <summary>
            /// Serialize the list of fields.
            /// </summary>
            /// <param name="contract">The resource contract that defines the fields to serialize.</param>
            /// <param name="entity">The instance to serialize the fields from.</param>
            /// <returns>The list of JSON values which represent the fields.</returns>
            IReadOnlyList<JsonMember> SerializeFields(IContract contract, object entity)
            {
                var fields = contract.Fields.Where(ShouldSerialize).ToList();

                return SerializeFields(fields, entity);
            }

            /// <summary>
            /// Serialize the list of fields.
            /// </summary>
            /// <param name="fields">The list of fields to serialize.</param>
            /// <param name="entity">The instance to serialize the fields from.</param>
            /// <returns>The list of JSON values which represent the fields.</returns>
            IReadOnlyList<JsonMember> SerializeFields(IEnumerable<IField> fields, object entity)
            {
                return fields.Select(field => SerializeField(field, entity)).ToList();
            }

            /// <summary>
            /// Build the JSON member for the given field.
            /// </summary>
            /// <param name="field">The field to build the JSON member for.</param>
            /// <param name="entity">The entity to extract the value for the JSON member.</param>
            /// <returns>The JSON member which represents the given field on the entity.</returns>
            JsonMember SerializeField(IField field, object entity)
            {
                if (field.Is(FieldOptions.Relationship | FieldOptions.SerializeAsEmbedded))
                {
                    // the IJsonConvertFactory will delegate complex objects back to the serializer so they can be serialized using the JSON API format
                    var jsonSerializer = new JsonSerializer(new JsonConverterFactory(this), _jsonSerializer.FieldNamingStrategy);

                    return new JsonMember(jsonSerializer.FieldNamingStrategy.GetName(field.Name), jsonSerializer.SerializeValue(field.GetValue(entity)));
                }
                
                return new JsonMember(_jsonSerializer.FieldNamingStrategy.GetName(field.Name), _jsonSerializer.SerializeValue(field.GetValue(entity)));
            }

            /// <summary>
            /// Serialize the list of relationships for the contract.
            /// </summary>
            /// <param name="contract">The contract to serialize the fields from.</param>
            /// <param name="entity">The instance to serialize the relationships from.</param>
            /// <returns>The list of JSON values which represent the relationships.</returns>
            IReadOnlyList<JsonMember> SerializeRelationships(IContract contract, object entity)
            {
                var relationships = contract.Relationships(ShouldSerialize).ToList();

                return SerializeRelationships(relationships, entity);
            }

            /// <summary>
            /// Serialize the list of relationships.
            /// </summary>
            /// <param name="relationships">The list of relationships to serialize.</param>
            /// <param name="entity">The instance to serialize the relationships from.</param>
            /// <returns>The list of JSON values which represent the relationships.</returns>
            IReadOnlyList<JsonMember> SerializeRelationships(IEnumerable<IRelationship> relationships, object entity)
            {
                var members = relationships.Select(relationship => SerializeRelationship(relationship, entity));

                members = members.Where(member => ((JsonObject)member.Value).Members.Count > 0);

                return members.ToList();
            }

            /// <summary>
            /// Build a member that represents the relationship.
            /// </summary>
            /// <param name="relationship">The relationship that the member is being built for.</param>
            /// <param name="entity">The entity to build the relationship for.</param>
            /// <returns>The member that represents the relationship for the given entity.</returns>
            JsonMember SerializeRelationship(IRelationship relationship, object entity)
            {
                var members = new List<JsonMember>();

                if (relationship.UriTemplate != null)
                {
                    var uri = relationship.UriTemplate.Bind(entity);

                    members.Add(
                        new JsonMember(
                            "links",
                            new JsonObject(new JsonMember("related", new JsonString(uri)))));
                }

                if (ShouldSerialize(relationship) && relationship.IsNot(FieldOptions.SerializeAsEmbedded))
                {
                    var data = SerializeRelationshipData(relationship, entity);

                    if (data != null)
                    {
                        members.Add(new JsonMember("data", data));
                    }
                }

                return new JsonMember(_jsonSerializer.FieldNamingStrategy.GetName(relationship.Name), new JsonObject(members));
            }

            /// <summary>
            /// Serialize the relationship data.
            /// </summary>
            /// <param name="relationship">The relationship that the member is being built for.</param>
            /// <param name="entity">The entity to build the relationship for.</param>
            /// <returns>The JSON value that represents the actual relationship data, or null if no data link can be created.</returns>
            JsonValue SerializeRelationshipData(IRelationship relationship, object entity)
            {
                IContract contract;
                if (_contractResolver.TryResolve(relationship.RelatedTo, out contract) == false)
                {
                    throw new JsonApiException(
                        "Could not find the related type '{0}' for the relationship '{1}'.", relationship.RelatedTo, relationship.Name);
                }

                if (relationship.Type == RelationshipType.BelongsTo)
                {
                    return SerializeBelongsTo((IBelongsToRelationship)relationship, contract, entity);
                }

                return SerializeHasMany((IHasManyRelationship)relationship, contract, entity);
            }

            /// <summary>
            /// Serialize a BelongsTo relationship.
            /// </summary>
            /// <param name="relationship">The relationship to serialize.</param>
            /// <param name="contract">The contract of the related entity.</param>
            /// <param name="entity">The entity instance to serialize the relationship from.</param>
            /// <returns>The value that represents the data node for the relationship.</returns>
            JsonValue SerializeBelongsTo(IBelongsToRelationship relationship, IContract contract, object entity)
            {
                if (relationship.Is(FieldOptions.Serializable))
                {
                    return SerializeBelongsToEntity(relationship, contract, entity);
                }

                return SerializeBelongsToBackingField(relationship, contract, entity);
            }

            /// <summary>
            /// Serialize a BelongsTo relationship.
            /// </summary>
            /// <param name="relationship">The relationship to serialize.</param>
            /// <param name="contract">The contract of the related entity.</param>
            /// <param name="entity">The entity instance to serialize the relationship from.</param>
            /// <returns>The value that represents the data node for the relationship.</returns>
            JsonValue SerializeBelongsToEntity(IBelongsToRelationship relationship, IContract contract, object entity)
            {
                var value = relationship.GetValue(entity);

                if (value == null)
                {
                    return SerializeBelongsToBackingField(relationship, contract, entity);
                }

                return new JsonObject(SerializeResourceKey(contract, value));
            }

            /// <summary>
            /// Serialize a BelongsTo relationship via the backing field.
            /// </summary>
            /// <param name="relationship">The relationship to serialize.</param>
            /// <param name="contract">The contract of the related entity.</param>
            /// <param name="entity">The entity instance to serialize the relationship from.</param>
            /// <returns>The value that represents the data node for the relationship.</returns>
            JsonValue SerializeBelongsToBackingField(IBelongsToRelationship relationship, IContract contract, object entity)
            {
                var value = relationship.BackingField?.GetValue(entity);

                if (value == null)
                {
                    return null;
                }

                return new JsonObject(SerializeResourceKey(contract, _jsonSerializer.SerializeValue(value)));
            }

            /// <summary>
            /// Serialize a HasMany relationship.
            /// </summary>
            /// <param name="relationship">The relationship to serialize.</param>
            /// <param name="contract">The contract of the related entity.</param>
            /// <param name="entity">The entity instance to serialize the relationship from.</param>
            /// <returns>The value that represents the data node for the relationship.</returns>
            JsonValue SerializeHasMany(IHasManyRelationship relationship, IContract contract, object entity)
            {
                var value = relationship.GetValue(entity);

                if (value == null)
                {
                    return null;
                }

                if (TypeHelper.IsEnumerable(value.GetType()) == false)
                {
                    throw new JsonApiException("Can not serialize a HasMany relationship from type that doesnt support IEnumerable.");
                }

                return new JsonArray(SerializeHasMany(contract, (IEnumerable)value).ToList());
            }

            /// <summary>
            /// Serialize the collection to entities into a relationship data node.
            /// </summary>
            /// <param name="contract">The contract of the items in the collection.</param>
            /// <param name="collection">The collection of items to serialize.</param>
            /// <returns>The list of JSON object which represent the entity keys.</returns>
            IEnumerable<JsonObject> SerializeHasMany(IContract contract, IEnumerable collection)
            {
                foreach (var item in collection)
                {
                    yield return new JsonObject(SerializeResourceKey(contract, item));
                }
            }

            /// <summary>
            /// Serialize an resource key.
            /// </summary>
            /// <param name="contract">The contract to serialize the key for.</param>
            /// <param name="value">The value to serialize the key for.</param>
            /// <returns>The JSON object that represents the entity key.</returns>
            IReadOnlyList<JsonMember> SerializeResourceKey(IContract contract, object value)
            {
                if (value != null && TypeHelper.IsReferenceType(value.GetType()))
                {
                    var field = contract.Fields(FieldOptions.Id).SingleOrDefault();

                    if (field != null)
                    {
                        value = field.GetValue(value);
                    }
                }

                return SerializeResourceKey(contract, _jsonSerializer.SerializeValue(value));
            }

            /// <summary>
            /// Serialize an resource key.
            /// </summary>
            /// <param name="contract">The contract to serialize the key for.</param>
            /// <param name="value">The JSON value that identifies the ID.</param>
            /// <returns>The JSON object that represents the entity key.</returns>
            IReadOnlyList<JsonMember> SerializeResourceKey(IContract contract, JsonValue value)
            {
                var members = new List<JsonMember>
                {
                    new JsonMember("type", new JsonString(contract.Name))
                };

                if (IsNotNull(value))
                {
                    members.Add(new JsonMember("id", value));
                }

                return members;
            }

            /// <summary>
            /// Serialize the list of included entities for the given top level primary entities.
            /// </summary>
            /// <param name="entities">The list of top level entities to serialize the included entities for.</param>
            /// <returns>The list of JSON objects that represent the included entities.</returns>
            internal IEnumerable<JsonObject> SerializeIncluded(IEnumerable entities)
            {
                var included = new List<JsonObject>();

                foreach (var entity in entities)
                {
                    IContract contract;
                    if (_contractResolver.TryResolve(entity.GetType(), out contract) == false)
                    {
                        throw new JsonApiException("Could not find the entity type for the CLR type of '{0}'.", entity.GetType());
                    }

                    included.AddRange(SerializeIncluded(contract.Relationships(), entity));
                }

                return included;
            }

            /// <summary>
            /// Serialize the relationships on the given entity.
            /// </summary>
            /// <param name="relationships">The list of relationships to serialize the included types from.</param>
            /// <param name="entity">The entity to serialize from.</param>
            /// <returns>The list of types to include for the entity.</returns>
            IEnumerable<JsonObject> SerializeIncluded(IEnumerable<IRelationship> relationships, object entity)
            {
                var included = new List<JsonObject>();

                foreach (var relationship in relationships.Where(ShouldSerialize))
                {
                    included.AddRange(SerializeIncluded(relationship, entity));
                }

                return included;
            }

            /// <summary>
            /// Serialize the list of included items from the relationship.
            /// </summary>
            /// <param name="relationship">The relationship that defines the related items.</param>
            /// <param name="entity">The entity to serialize the included items from.</param>
            /// <returns>The list of types to include for the entity.</returns>
            IEnumerable<JsonObject> SerializeIncluded(IRelationship relationship, object entity)
            {
                IContract contract;
                if (_contractResolver.TryResolve(relationship.RelatedTo, out contract) == false)
                {
                    throw new JsonApiException(
                        "Could not find the related type '{0}' for the relationship '{1}'.", relationship.RelatedTo, relationship.Name);
                }

                if (relationship.Type == RelationshipType.HasMany)
                {
                    return SerializeIncludedHasMany((IHasManyRelationship)relationship, contract, entity);
                }

                return SerializeIncludedBelongsTo((IBelongsToRelationship)relationship, contract, entity);
            }

            /// <summary>
            /// Serialize the list of included items from the HasMany relationship.
            /// </summary>
            /// <param name="relationship">The relationship that defines the related items.</param>
            /// <param name="contract">The contract of the related items.</param>
            /// <param name="entity">The entity to serialize the included items from.</param>
            /// <returns>The list of types to include for the entity.</returns>
            IEnumerable<JsonObject> SerializeIncludedHasMany(IHasManyRelationship relationship, IContract contract, object entity)
            {
                var collection = relationship.GetValue(entity);

                if (collection == null)
                {
                    return new JsonObject[0];
                }

                if (TypeHelper.IsEnumerable(collection.GetType()) == false)
                {
                    throw new JsonApiException("Can not serialize a HasMany relationship from type that doesnt support IEnumerable.");
                }

                if (relationship.Is(FieldOptions.SerializeAsEmbedded))
                {
                    var items = new List<JsonObject>();

                    foreach (var e in (IEnumerable) collection)
                    {
                        items.AddRange(SerializeIncluded(contract.Relationships(), e));
                    }

                    return items;
                }

                return SerializeIncluded(contract, (IEnumerable)collection);
            }

            /// <summary>
            /// Serialize the list of included items from the BelongsTo relationship.
            /// </summary>
            /// <param name="relationship">The relationship that defines the related items.</param>
            /// <param name="contract">The contract of the related items.</param>
            /// <param name="entity">The entity to serialize the included items from.</param>
            /// <returns>The list of types to include for the entity.</returns>
            IEnumerable<JsonObject> SerializeIncludedBelongsTo(IBelongsToRelationship relationship, IContract contract, object entity)
            {
                if (relationship.IsNot(FieldOptions.Serializable))
                {
                    return new JsonObject[0];
                }

                var value = relationship.GetValue(entity);

                if (value == null || TypeHelper.IsReferenceType(value.GetType()) == false)
                {
                    return new JsonObject[0];
                }

                if (relationship.Is(FieldOptions.SerializeAsEmbedded))
                {
                    return SerializeIncluded(contract.Relationships(), value);
                }

                return SerializeIncluded(contract, value);
            }

            /// <summary>
            /// Serialize the list of included entities starting from the top level.
            /// </summary>
            /// <param name="contract">The contract for the entities in the collection.</param>
            /// <param name="collection">The collection of entities to serialize the included items from.</param>
            /// <returns>The list of JSON objects that represent the included fields.</returns>
            IEnumerable<JsonObject> SerializeIncluded(IContract contract, IEnumerable collection)
            {
                var included = new List<JsonObject>();

                foreach (var entity in collection)
                {
                    included.AddRange(SerializeIncluded(contract, entity));
                }

                return included;
            }

            /// <summary>
            /// Serialize the entity as an included type.
            /// </summary>
            /// <param name="contract">The resource contract.</param>
            /// <param name="entity">The entity to serialize as an included type.</param>
            /// <returns>The list of types to included for the entity.</returns>
            IEnumerable<JsonObject> SerializeIncluded(IContract contract, object entity)
            {
                var jsonObject = SerializeEntity(contract, entity);

                if (HasVisited(jsonObject))
                {
                    return new JsonObject[0];
                }

                Visit(jsonObject);

                return new[] { jsonObject }.Union(SerializeIncluded(contract.Relationships(), entity));
            }

            /// <summary>
            /// Returns a value indicating whether or not the given field should be included when serializing.
            /// </summary>
            /// <param name="field">The field to determine whether or not it should be included.</param>
            /// <returns>true if the field should be included, false if not.</returns>
            static bool ShouldSerialize(IField field)
            {
                if (field.IsNot(FieldOptions.Relationship))
                {
                    return field.IsNot(FieldOptions.Id) && field.IsNot(FieldOptions.BackingField) && field.Is(FieldOptions.Serializable);
                }

                return field.Is(FieldOptions.SerializeAsEmbedded);
            }

            /// <summary>
            /// Returns a value indicating whether the given relationship can be serialized.
            /// </summary>
            /// <param name="relationship">The relationship to determine whether it can be serialized.</param>
            /// <returns>true if the relationship can be serialized, false if not.</returns>
            static bool ShouldSerialize(IRelationship relationship)
            {
                if (relationship.RelatedTo == null)
                {
                    return false;
                }

                if (relationship.IsNot(FieldOptions.Serializable) && relationship.Type == RelationshipType.BelongsTo)
                {
                    var belongsToRelationship = (IBelongsToRelationship) relationship;

                    return belongsToRelationship.BackingField != null && belongsToRelationship.BackingField.Is(FieldOptions.Serializable);
                }

                return relationship.IsNot(FieldOptions.Id) && relationship.Is(FieldOptions.Serializable);
            }

            /// <summary>
            /// Returns a value indicating whether the JSON Member has a non-null value.
            /// </summary>
            /// <param name="jsonMember">The JSON member to test.</param>
            /// <returns>true if the JSON member has a non-null value, false if not.</returns>
            static bool IsNotNull(JsonMember jsonMember)
            {
                return IsNotNull(jsonMember.Value);
            }

            /// <summary>
            /// Returns a value indicating whether the JSON value has a non-null value.
            /// </summary>
            /// <param name="jsonValue">The JSON value to test.</param>
            /// <returns>true if the JSON value has a non-null value, false if not.</returns>
            static bool IsNotNull(JsonValue jsonValue)
            {
                return jsonValue != null && jsonValue.GetType() != typeof(JsonNull);
            }

            /// <summary>
            /// Serialize the value.
            /// </summary>
            /// <param name="serializer">The serializer to utilize when serializing nested objects.</param>
            /// <param name="type">The CLR type of the value to serialize.</param>
            /// <param name="value">The value to serialize.</param>
            /// <returns>The JSON value that represents the given CLR value.</returns>
            /// <remarks>This is called by the JsonSerializer when it is to serialize an embedded field/relationship which is a complex object.</remarks>
            JsonValue IJsonConverter.SerializeValue(IJsonSerializer serializer, Type type, object value)
            {
                IContract contract;
                if (_contractResolver.TryResolve(type, out contract) == false)
                {
                    throw new JsonApiException("Could not resolve the contract with the CLR type of '{0}'.", type);
                }

                return SerializeEntity(contract, value);
            }

            /// <summary>
            /// Deserialize a JSON value to a defined CLR type.
            /// </summary>
            /// <param name="serializer">The serializer to utilize when deserializing nested objects.</param>
            /// <param name="type">The CLR type to deserialize the JSON value to.</param>
            /// <param name="jsonValue">The JSON value to deserialize.</param>
            /// <returns>The object that represents the CLR version of the given JSON value.</returns>
            object IJsonConverter.DeserializeValue(IJsonSerializer serializer, Type type, JsonValue jsonValue)
            {
                // this will never get called under this context
                throw new NotImplementedException();
            }

            /// <summary>
            /// Returns a value indicating whether or not the converter can convert the given type.
            /// </summary>
            /// <param name="type">The type to convert.</param>
            /// <returns>true if the type can be converted by this converter, false if not.</returns>
            bool IJsonConverter.CanConvert(Type type)
            {
                return _contractResolver.CanResolve(type);
            }
        }

        #endregion

        #region Deserializer

        class Deserializer : IJsonConverter
        {
            readonly JsonObject _rootObject;
            readonly IContractResolver _contractResolver;
            readonly IJsonApiEntityCache _instanceCache;
            readonly IJsonSerializer _jsonSerializer;

            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="rootObject">The root JSON object that contains both the 'data' and the 'include' nodes.</param>
            /// <param name="contractResolver">The resource contract resolver.</param>
            /// <param name="fieldNamingStrategy">The field naming strategy for deserialization.</param>
            /// <param name="instanceCache">The entity cache to use for reusing existing instances in the object graph.</param>
            internal Deserializer(JsonObject rootObject, IContractResolver contractResolver, IFieldNamingStrategy fieldNamingStrategy, IJsonApiEntityCache instanceCache)
            {
                _rootObject = rootObject;
                _contractResolver = contractResolver;
                _instanceCache = instanceCache;
                _jsonSerializer = new JsonSerializer(new JsonConverterFactory(), fieldNamingStrategy);
            }

            /// <summary>
            /// Deserialize a collection of items.
            /// </summary>
            /// <returns>The list of items that was deserialized.</returns>
            public IEnumerable<object> DeserializeMany()
            {
                var jsonArray = _rootObject["data"] as JsonArray;

                if (jsonArray == null)
                {
                    throw new JsonApiException("Can not return a sequence of items as the top level value is only a single entity.");
                }

                return jsonArray.OfType<JsonObject>().Select(DeserializeEntity);
            }

            /// <summary>
            /// Deserialize the root level JSON object into a CLR type.
            /// </summary>
            /// <returns>The instance that was created.</returns>
            internal object DeserializeEntity()
            {
                var jsonObject = _rootObject["data"] as JsonObject;

                if (jsonObject == null)
                {
                    throw new JsonApiException("Can not return a single item as the top level value is an array.");
                }

                return DeserializeEntity(jsonObject);
            }

            /// <summary>
            /// Deserialize a JSON object into a CLR type.
            /// </summary>
            /// <param name="jsonObject">The JSON object to deserialize into a CLR type.</param>
            /// <returns>The instance that was created.</returns>
            object DeserializeEntity(JsonObject jsonObject)
            {
                // first check to see if the object has been resolved by another operation
                object entity;
                if (TryResolveFromCache(jsonObject, out entity))
                {
                    return entity;
                }

                var typeAttribute = jsonObject["type"];

                IContract contract;
                if (_contractResolver.TryResolve(((JsonString)typeAttribute).Value, out contract) == false)
                {
                    throw new JsonApiException("Could not find a type for '{0}'.", ((JsonString)typeAttribute).Value);
                }

                return DeserializeEntity(contract, jsonObject);
            }

            /// <summary>
            /// Attempt to resolve the entity with the given entity key.
            /// </summary>
            /// <param name="key">The entity key to resolve the entity for.</param>
            /// <param name="entity">The entity that was resolved for the given entity key.</param>
            /// <returns>true if an entity could be resolved, false if not.</returns>
            bool TryResolveFromCache(JsonObject key, out object entity)
            {
                return _instanceCache.TryGetValue(key, out entity);
            }

            /// <summary>
            /// Attempt to resolve the entity with the given entity key.
            /// </summary>
            /// <param name="key">The entity key to resolve the entity for.</param>
            /// <param name="entity">The entity that was resolved for the given entity key.</param>
            /// <returns>true if an entity could be resolved, false if not.</returns>
            bool TryResolve(JsonObject key, out object entity)
            {
                if (TryResolveFromCache(key, out entity))
                {
                    return true;
                }

                var included = _rootObject["included"] as JsonArray;
                if (included != null)
                {
                    if (TryResolveFromList(key, included.OfType<JsonObject>(), out entity))
                    {
                        return true;
                    }
                }

                var data = _rootObject["data"] as JsonArray;
                if (data != null)
                {
                    if (TryResolveFromList(key, data.OfType<JsonObject>(), out entity))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Attempt to resolve the entity with the given entity key.
            /// </summary>
            /// <param name="key">The entity key to resolve the entity for.</param>
            /// <param name="jsonObjects">The list of JSON objects to attempt to resolve from.</param>
            /// <param name="entity">The entity that was resolved for the given entity key.</param>
            /// <returns>true if an entity could be resolved, false if not.</returns>
            bool TryResolveFromList(JsonObject key, IEnumerable<JsonObject> jsonObjects, out object entity)
            {
                entity = null;

                var jsonObject = jsonObjects.FirstOrDefault(j => JsonApiEntityKeyEqualityComparer.Instance.Equals(key, j));

                if (jsonObject != null)
                {
                    entity = DeserializeEntity(jsonObject);
                }

                return ReferenceEquals(entity, null) == false;
            }

            /// <summary>
            /// Deserialize an object.
            /// </summary>
            /// <param name="contract">The contract of the object to deserialize.</param>
            /// <param name="jsonObject">The JSON object that represents the object to deserialize.</param>
            /// <returns>The instance that was deserialized.</returns>
            object DeserializeEntity(IContract contract, JsonObject jsonObject)
            {
                var entity = contract.CreateInstance();

                if (_instanceCache.TryAdd(jsonObject, entity) == false)
                {
                    // TODO: not sure if anything should be done here
                }

                DeserializeEntity(contract, jsonObject, entity);

                return entity;
            }

            /// <summary>
            /// Deserialize an object.
            /// </summary>
            /// <param name="contract">The contract type of the object to deserialize.</param>
            /// <param name="jsonObject">The JSON object that represents the object to deserialize.</param>
            /// <param name="entity">The entity instance to deserialize the fields into.</param>
            internal void DeserializeEntity(IContract contract, JsonObject jsonObject, object entity)
            {
                var attribute = jsonObject["id"];

                if (attribute != null)
                {
                    var field = contract.Fields(FieldOptions.Id).SingleOrDefault();
                    if (field != null && field.Is(FieldOptions.Deserializable))
                    {
                        DeserializeField(field, attribute, entity);
                    }
                }

                var attributes = jsonObject["attributes"] as JsonObject;
                if (attributes != null)
                {
                    DeserializeFields(contract.Fields(ShouldDeserialize).ToList(), attributes.Members, entity);
                }

                var relationships = jsonObject["relationships"] as JsonObject;
                if (relationships != null)
                {
                    DeserializeRelationships(contract.Relationships(ShouldDeserialize).ToList(), relationships.Members, entity);
                }
            }

            /// <summary>
            /// Deserialize a list of fields.
            /// </summary>
            /// <param name="fields">The list of allowable fields for deserialization.</param>
            /// <param name="members">The list of members to deserialize the values from.</param>
            /// <param name="entity">The entity to deserialize the values to.</param>
            void DeserializeFields(IReadOnlyList<IField> fields, IEnumerable<JsonMember> members, object entity)
            {
                foreach (var member in members)
                {
                    var field = fields.SingleOrDefault(f => String.Equals(f.Name, _jsonSerializer.FieldNamingStrategy.ResolveName(member.Name), StringComparison.OrdinalIgnoreCase));

                    if (field != null)
                    {
                        DeserializeField(field, member.Value, entity);
                    }
                }
            }

            /// <summary>
            /// Deserialize a field.
            /// </summary>
            /// <param name="field">The field to set on the entity.</param>
            /// <param name="value">The JSON value to set on the entity.</param>
            /// <param name="entity">The entity to set the value on.</param>
            void DeserializeField(IField field, JsonValue value, object entity)
            {
                if (field.Is(FieldOptions.Deserializable) == false || ReferenceEquals(value, JsonNull.Instance))
                {
                    return;
                }

                if (field.Is(FieldOptions.Relationship | FieldOptions.DeserializeAsEmbedded))
                {
                    // the IJsonConvertFactory will delegate complex objects back to the serializer so they can be deserialized using the JSON API format
                    var jsonSerializer = new JsonSerializer(new JsonConverterFactory(this), _jsonSerializer.FieldNamingStrategy);

                    field.SetValue(entity, jsonSerializer.DeserializeValue(field.Accessor.ValueType, value));

                    return;
                }

                field.SetValue(entity, _jsonSerializer.DeserializeValue(field.Accessor.ValueType, value));
            }

            /// <summary>
            /// Deserialize a list of relationships.
            /// </summary>
            /// <param name="relationships">The list of allowable relationships for deserialization.</param>
            /// <param name="members">The list of members to deserialize the values from.</param>
            /// <param name="entity">The entity to deserialize the values to.</param>
            void DeserializeRelationships(IReadOnlyList<IRelationship> relationships, IEnumerable<JsonMember> members, object entity)
            {
                foreach (var member in members.Where(HasDataMember))
                {
                    var relationship = relationships.SingleOrDefault(r => String.Equals(r.Name, _jsonSerializer.FieldNamingStrategy.ResolveName(member.Name), StringComparison.OrdinalIgnoreCase));

                    if (relationship == null)
                    {
                        continue;
                    }

                    var data = ((JsonObject)member.Value)["data"];

                    if (relationship.Type == RelationshipType.HasMany)
                    {
                        DeserializeHasMany((IHasManyRelationship)relationship, (JsonArray)data, entity);
                        continue;
                    }

                    DeserializeBelongsTo((IBelongsToRelationship)relationship, (JsonObject)data, entity);
                }
            }

            /// <summary>
            /// Returns true if the given member contains a nested object which has a data member.
            /// </summary>
            /// <param name="jsonMember">The JSON member to test against.</param>
            /// <returns>true if the given JSON member contains a nested object which contains a data member.</returns>
            static bool HasDataMember(JsonMember jsonMember)
            {
                var jsonObject = jsonMember.Value as JsonObject;

                return jsonObject?["data"] != null;
            }

            /// <summary>
            /// Deserialize a BelongsTo relationship.
            /// </summary>
            /// <param name="relationship">The relationship to set on the entity.</param>
            /// <param name="value">The JSON value to set on the entity.</param>
            /// <param name="entity">The entity to set the value on.</param>
            void DeserializeBelongsTo(IBelongsToRelationship relationship, JsonObject value, object entity)
            {
                if (relationship.BackingField != null)
                {
                    var member = value["id"];

                    if (member != null)
                    {
                        DeserializeField(relationship.BackingField, member, entity);
                    }
                }

                if (relationship.Accessor != null && relationship.Accessor.CanWrite)
                {
                    object related;
                    if (TryResolve(value, out related))
                    {
                        relationship.SetValue(entity, related);
                    }
                }
            }

            /// <summary>
            /// Deserialize a HasMany relationship.
            /// </summary>
            /// <param name="relationship">The relationship to set on the entity.</param>
            /// <param name="value">The JSON value to set on the entity.</param>
            /// <param name="entity">The entity to set the value on.</param>
            void DeserializeHasMany(IHasManyRelationship relationship, JsonArray value, object entity)
            {
                if (relationship.Accessor == null || relationship.Accessor.CanWrite == false)
                {
                    return;
                }

                var collection = CreateListInstance(relationship.Accessor.ValueType);

                if (collection == null)
                {
                    throw new JsonApiException(
                        $"Can not deserialize the related collection as an appropriate IList instance could not be created for '{relationship.Accessor.ValueType}'.");
                }

                foreach (var jsonObject in value.OfType<JsonObject>())
                {
                    object related;
                    if (TryResolve(jsonObject, out related))
                    {
                        collection.Add(related);
                    }
                }

                relationship.Accessor.SetValue(entity, collection);
            }

            /// <summary>
            /// Creates an instance of a list to hold the related items.
            /// </summary>
            /// <param name="type">The type to create the list for.</param>
            /// <returns>The list was created.</returns>
            static IList CreateListInstance(Type type)
            {
                if (TypeHelper.IsList(type))
                {
                    return Activator.CreateInstance(type) as IList;
                }

                if (type.GetTypeInfo().IsInterface)
                {
                    var definition = type.GetGenericTypeDefinition();

                    if (definition == typeof (IReadOnlyList<>) || definition == typeof (IList<>) || definition == typeof(ICollection<>))
                    {
                        type = typeof (List<>).MakeGenericType(TypeHelper.GetUnderlyingType(type));

                        return Activator.CreateInstance(type) as IList;
                    }
                }

                return null;
            }

            /// <summary>
            /// Returns a value indicating whether or not the given field should be included when deserializing.
            /// </summary>
            /// <param name="field">The field to determine whether or not it should be included.</param>
            /// <returns>true if the field should be included, false if not.</returns>
            static bool ShouldDeserialize(IField field)
            {
                return field.IsNot(FieldOptions.Id | FieldOptions.Relationship) && field.Is(FieldOptions.Deserializable);
            }

            /// <summary>
            /// Returns a value indicating whether or not the given relationship should be included when deserializing.
            /// </summary>
            /// <param name="relationship">The relationship to determine whether or not it should be included.</param>
            /// <returns>true if the relationship should be included, false if not.</returns>
            static bool ShouldDeserialize(IRelationship relationship)
            {
                return relationship.Is(FieldOptions.Relationship | FieldOptions.Deserializable) && relationship.IsNot(FieldOptions.DeserializeAsEmbedded);
            }

            /// <summary>
            /// Serialize the value.
            /// </summary>
            /// <param name="serializer">The serializer to utilize when serializing nested objects.</param>
            /// <param name="type">The CLR type of the value to serialize.</param>
            /// <param name="value">The value to serialize.</param>
            /// <returns>The JSON value that represents the given CLR value.</returns>
            JsonValue IJsonConverter.SerializeValue(IJsonSerializer serializer, Type type, object value)
            {
                // this should never get called under this context.
                throw new NotImplementedException();
            }

            /// <summary>
            /// Deserialize a JSON value to a defined CLR type.
            /// </summary>
            /// <param name="serializer">The serializer to utilize when deserializing nested objects.</param>
            /// <param name="type">The CLR type to deserialize the JSON value to.</param>
            /// <param name="jsonValue">The JSON value to deserialize.</param>
            /// <returns>The object that represents the CLR version of the given JSON value.</returns>
            object IJsonConverter.DeserializeValue(IJsonSerializer serializer, Type type, JsonValue jsonValue)
            {
                IContract contract;
                if (_contractResolver.TryResolve(type, out contract) == false)
                {
                    throw new JsonApiException("Could not resolve the contract with the CLR type of '{0}'.", type);
                }

                return DeserializeEntity(contract, (JsonObject)jsonValue);
            }

            /// <summary>
            /// Returns a value indicating whether or not the converter can convert the given type.
            /// </summary>
            /// <param name="type">The type to convert.</param>
            /// <returns>true if the type can be converted by this converter, false if not.</returns>
            bool IJsonConverter.CanConvert(Type type)
            {
                return _contractResolver.CanResolve(type);
            }
        }

        #endregion
    }
}
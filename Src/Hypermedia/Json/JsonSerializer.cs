﻿using System;
using System.Collections.Generic;
using JsonLite.Ast;

namespace Hypermedia.Json
{
    public sealed class JsonSerializer : IJsonSerializer
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public JsonSerializer() : this(new JsonConverterFactory(), new DefaultFieldNamingStrategy()) { }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="jsonConverterFactory">The JSON converter factory.</param>
        public JsonSerializer(IJsonConverterFactory jsonConverterFactory) : this(jsonConverterFactory, new DefaultFieldNamingStrategy()) { }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="jsonConverterFactory">The JSON converter factory.</param>
        /// <param name="fieldNamingStrategy">The field naming strategy.</param>
        public JsonSerializer(IJsonConverterFactory jsonConverterFactory, IFieldNamingStrategy fieldNamingStrategy)
        {
            JsonConverterFactory = jsonConverterFactory;
            FieldNamingStrategy = fieldNamingStrategy;
        }

        /// <summary>
        /// Serialize an inline object.
        /// </summary>
        /// <param name="value">The value to serialization inline.</param>
        /// <returns>The JSON value which represents the inline serialization of the value.</returns>
        public JsonValue SerializeValue(object value)
        {
            return new Serializer(JsonConverterFactory, FieldNamingStrategy).SerializeValue(value);
        }

        /// <summary>
        /// Deserialize the given JSON value according to the specified CLR type.
        /// </summary>
        /// <param name="type">The CLR type to deserialize the JSON value to.</param>
        /// <param name="jsonValue">The JSON value to deserialize.</param>
        /// <returns>The CLR object that the JSON value was deserialized from.</returns>
        public object DeserializeValue(Type type, JsonValue jsonValue)
        {
            if (ReferenceEquals(jsonValue, JsonNull.Instance))
            {
                return null;
            }

            var converter = JsonConverterFactory.CreateInstance(type);

            return converter.DeserializeValue(this, type, jsonValue);
        }

        /// <summary>
        /// The JSON converter factory.
        /// </summary>
        public IJsonConverterFactory JsonConverterFactory { get; }

        /// <summary>
        /// The field naming strategy.
        /// </summary>
        public IFieldNamingStrategy FieldNamingStrategy { get; }

        #region Serializer

        class Serializer : IJsonSerializer
        {
            readonly Stack<object> _visited = new Stack<object>();

            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="jsonConverterFactory">The JSON converter factory.</param>
            /// <param name="fieldNamingStrategy">The field naming strategy.</param>
            public Serializer(IJsonConverterFactory jsonConverterFactory, IFieldNamingStrategy fieldNamingStrategy)
            {
                JsonConverterFactory = jsonConverterFactory;
                FieldNamingStrategy = fieldNamingStrategy;
            }

            /// <summary>
            /// Serialize an inline object.
            /// </summary>
            /// <param name="value">The value to serialization inline.</param>
            /// <returns>The JSON value which represents the inline serialization of the value.</returns>
            public JsonValue SerializeValue(object value)
            {
                if (ReferenceEquals(value, null) || _visited.Contains(value))
                {
                    return JsonNull.Instance;
                }

                _visited.Push(value);

                var type = value.GetType();
                var converter = JsonConverterFactory.CreateInstance(type);

                var jsonValue = converter.SerializeValue(this, type, value);

                _visited.Pop();

                return jsonValue;
            }

            /// <summary>
            /// Deserialize the given JSON value according to the specified CLR type.
            /// </summary>
            /// <param name="type">The CLR type to deserialize the JSON value to.</param>
            /// <param name="jsonValue">The JSON value to deserialize.</param>
            /// <returns>The CLR object that the JSON value was deserialized from.</returns>
            object IJsonSerializer.DeserializeValue(Type type, JsonValue jsonValue)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// The JSON converter factory.
            /// </summary>
            public IJsonConverterFactory JsonConverterFactory { get; }

            /// <summary>
            /// The field naming strategy.
            /// </summary>
            public IFieldNamingStrategy FieldNamingStrategy { get; }
        }

        #endregion
    }
}

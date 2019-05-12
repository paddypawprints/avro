/**
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Avro;
using Avro.IO;
using Avro.Generic;
using Avro.Specific;
using Newtonsoft.Json.Linq;

namespace Avro.POCO
{
        /// <summary>
    /// Reader wrapper class for reading data and storing into specific classes
    /// </summary>
    /// <typeparam name="T">Specific class type</typeparam>
    public class POCOReader<T> : DatumReader<T>
    {
        /// <summary>
        /// Reader class for reading data and storing into specific classes
        /// </summary>
        private readonly POCODefaultReader reader;

        /// <summary>
        /// Schema for the writer class
        /// </summary>
        public Schema WriterSchema { get { return reader.WriterSchema; } }

        /// <summary>
        /// Schema for the reader class
        /// </summary>
        public Schema ReaderSchema { get { return reader.ReaderSchema; } }

        /// <summary>
        /// Constructs a generic reader for the given schemas using the DefaultReader. If the
        /// reader's and writer's schemas are different this class performs the resolution.
        /// </summary>
        /// <param name="writerSchema">The schema used while generating the data</param>
        /// <param name="readerSchema">The schema desired by the reader</param>
        public POCOReader(Schema writerSchema, Schema readerSchema)
        {
            reader = new POCODefaultReader(typeof(T), writerSchema, readerSchema);
        }

        public POCOReader(POCODefaultReader reader)
        {
            this.reader = reader;
        }

        /// <summary>
        /// Generic read function
        /// </summary>
        /// <param name="reuse">object to store data read</param>
        /// <param name="dec">decorder to use for reading data</param>
        /// <returns></returns>
        public T Read(T reuse, Decoder dec)
        {
            return reader.Read(reuse, dec);
        }
    }
    /// <summary>
    /// Reader class for reading data and storing into specific classes
    /// </summary>
    public class POCODefaultReader : SpecificDefaultReader
    {
        public Type ListType { get => _listType; set => _listType = value; }
        public Type MapType { get => _mapType; set => _mapType = value; }
        private Type _listType = typeof(List<>);
        private Type _mapType = typeof(Dictionary<,>);
        public Func<Type, Object> RecordFactory = x=>Activator.CreateInstance(x);


        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="writerSchema">schema of the object that wrote the data</param>
        /// <param name="readerSchema">schema of the object that will store the data</param>
        public POCODefaultReader(Type objType, Schema writerSchema, Schema readerSchema)
            : base(writerSchema, readerSchema)
        {
            var rs = readerSchema as RecordSchema;
            if (rs != null)
            {
                ClassCache.LoadClassCache(objType, rs);
            }
        }

        /// <summary>
        /// Gets the string representation of the schema's data type
        /// </summary>
        /// <param name="schema">schema</param>
        /// <param name="nullable">flag to indicate union with null</param>
        /// <returns></returns>
        internal Type GetTypeFromSchema(Schema schema, bool nullable)
        {
            switch (schema.Tag)
            {
                case Schema.Type.Null:
                    return typeof(object);

                case Schema.Type.Boolean:
                    return nullable ? typeof(bool?) : typeof(bool);

                case Schema.Type.Int:
                    return nullable ? typeof(int?) : typeof(int);

                case Schema.Type.Long:
                    return nullable ? typeof(long?) : typeof(long);

                case Schema.Type.Float:
                    return nullable ? typeof(float?) : typeof(float);

                case Schema.Type.Double:
                    return nullable ? typeof(double?) : typeof(double);

                case Schema.Type.Fixed:
                case Schema.Type.Bytes:
                    return typeof(byte[]);

                case Schema.Type.String:
                    return typeof(string);

                case Schema.Type.Enumeration:
                    var namedSchema = schema as NamedSchema;
                    if (namedSchema == null)
                    {
                        throw new Exception("Unable to cast schema into a named schema");
                    }

                    Type enumType = null;
                    enumType = EnumCache.GetEnumeration(namedSchema);
                    if (enumType == null)
                    {
                        throw new Exception(string.Format("Couldn't find type matching enum name {0}", namedSchema.Fullname));
                    }

                    if (nullable)
                    {
                        return typeof(Nullable<>).MakeGenericType(new Type[] { enumType });
                    }
                    else
                    {
                        return enumType;
                    }

                case Schema.Type.Record:
                case Schema.Type.Error:
                    var recordSchema = schema as RecordSchema;
                    if (recordSchema == null)
                    {
                        throw new Exception("Unable to cast schema into a named schema");
                    }

                    Type recordtype = null;
                    recordtype = ClassCache.GetClass(recordSchema).GetClassType();
                    if (recordtype == null)
                    {
                        throw new Exception(string.Format("Couldn't find type matching schema name {0}", recordSchema.Fullname));
                    }

                    return recordtype;

                case Schema.Type.Array:
                    var arraySchema = schema as ArraySchema;
                    if (arraySchema == null)
                    {
                        throw new Exception("Unable to cast schema into an array schema");
                    }

                    return ListType.MakeGenericType(new Type[] { GetTypeFromSchema(arraySchema.ItemSchema, false) });

                case Schema.Type.Map:
                    var mapSchema = schema as MapSchema;
                    if (mapSchema == null)
                    {
                        throw new Exception("Unable to cast schema into a map schema");
                    }

                    return MapType.MakeGenericType(new Type[] { typeof(string), GetTypeFromSchema(mapSchema.ValueSchema, false) });

                case Schema.Type.Union:
                    var unionSchema = schema as UnionSchema;
                    if (unionSchema == null)
                    {
                        throw new Exception("Unable to cast schema into a union schema");
                    }

                    Schema nullibleType = CodeGen.getNullableType(unionSchema);
                    if (nullibleType == null)
                    {
                        return typeof(object);
                    }
                    else
                    {
                        return GetTypeFromSchema(nullibleType, true);
                    }
            }

            throw new Exception("Unable to generate CodeTypeReference for " + schema.Name + " type " + schema.Tag);
        }

        public object GetDefaultValue(Schema s, JToken defaultValue)
        {
            if (defaultValue == null)
            {
                return null;
            }

            switch (s.Tag)
            {
                case Schema.Type.Boolean:
                    if (defaultValue.Type != JTokenType.Boolean)
                    {
                        throw new AvroException("Default boolean value " + defaultValue.ToString() + " is invalid, expected is json boolean.");
                    }

                    return (bool)defaultValue;

                case Schema.Type.Int:
                    if (defaultValue.Type != JTokenType.Integer)
                    {
                        throw new AvroException("Default int value " + defaultValue.ToString() + " is invalid, expected is json integer.");
                    }

                    return Convert.ToInt32((int)defaultValue);

                case Schema.Type.Long:
                    if (defaultValue.Type != JTokenType.Integer)
                    {
                        throw new AvroException("Default long value " + defaultValue.ToString() + " is invalid, expected is json integer.");
                    }

                    return Convert.ToInt64((long)defaultValue);

                case Schema.Type.Float:
                    if (defaultValue.Type != JTokenType.Float)
                    {
                        throw new AvroException("Default float value " + defaultValue.ToString() + " is invalid, expected is json number.");
                    }

                    return (float)defaultValue;

                case Schema.Type.Double:
                    if (defaultValue.Type == JTokenType.Integer)
                    {
                        return Convert.ToDouble((int)defaultValue);
                    }
                    else if (defaultValue.Type == JTokenType.Float)
                    {
                        return Convert.ToDouble((float)defaultValue);
                    }
                    else
                    {
                        throw new AvroException("Default double value " + defaultValue.ToString() + " is invalid, expected is json number.");
                    }

                case Schema.Type.Bytes:
                    if (defaultValue.Type != JTokenType.String)
                    {
                        throw new AvroException("Default bytes value " + defaultValue.ToString() + " is invalid, expected is json string.");
                    }

                    var en = System.Text.Encoding.GetEncoding("iso-8859-1");
                    return en.GetBytes((string)defaultValue);

                case Schema.Type.Fixed:
                    if (defaultValue.Type != JTokenType.String)
                    {
                        throw new AvroException("Default fixed value " + defaultValue.ToString() + " is invalid, expected is json string.");
                    }

                    en = System.Text.Encoding.GetEncoding("iso-8859-1");
                    int len = (s as FixedSchema).Size;
                    byte[] bb = en.GetBytes((string)defaultValue);
                    if (bb.Length != len)
                    {
                        throw new AvroException("Default fixed value " + defaultValue.ToString() + " is not of expected length " + len);
                    }

                    return bb;

                case Schema.Type.String:
                    if (defaultValue.Type != JTokenType.String)
                    {
                        throw new AvroException("Default string value " + defaultValue.ToString() + " is invalid, expected is json string.");
                    }

                    return (string)defaultValue;

                case Schema.Type.Enumeration:
                    if (defaultValue.Type != JTokenType.String)
                    {
                        throw new AvroException("Default enum value " + defaultValue.ToString() + " is invalid, expected is json string.");
                    }

                    return (s as EnumSchema).Ordinal((string)defaultValue);

                case Schema.Type.Null:
                    if (defaultValue.Type != JTokenType.Null)
                    {
                        throw new AvroException("Default null value " + defaultValue.ToString() + " is invalid, expected is json null.");
                    }

                    return null;

                case Schema.Type.Array:
                    if (defaultValue.Type != JTokenType.Array)
                    {
                        throw new AvroException("Default array value " + defaultValue.ToString() + " is invalid, expected is json array.");
                    }

                    JArray jarr = defaultValue as JArray;
                    var array = (System.Collections.IList)Activator.CreateInstance(GetTypeFromSchema(s, false));

                    foreach (JToken jitem in jarr)
                    {
                        array.Add(GetDefaultValue((s as ArraySchema).ItemSchema, jitem));
                    }

                    return array;

                case Schema.Type.Record:
                case Schema.Type.Error:
                    if (defaultValue.Type != JTokenType.Object)
                    {
                        throw new AvroException($"Default record value {defaultValue.ToString()} is invalid, expected is json object.");
                    }

                    RecordSchema rcs = s as RecordSchema;
                    JObject jo = defaultValue as JObject;
                    var rec = RecordFactory(GetTypeFromSchema(rcs, false));
                    if (rec == null)
                    {
                        throw new Exception($"Couldn't create type matching schema name {rcs.Fullname}");
                    }

                    foreach (Field field in rcs)
                    {
                        JToken val = jo[field.Name];
                        if (val == null)
                            val = field.DefaultValue;
                        if (val == null)
                        {
                            throw new AvroException($"No default value for field {field.Name}");
                        }

                        ClassCache.GetClass(rcs).SetValue(rec, field, GetDefaultValue(field.Schema, val));
                    }

                    return rec;

                case Schema.Type.Map:
                    if (defaultValue.Type != JTokenType.Object)
                    {
                        throw new AvroException($"Default map value {defaultValue.ToString()} is invalid, expected is json object.");
                    }

                    jo = defaultValue as JObject;
                    var map = (System.Collections.IDictionary)Activator.CreateInstance(GetTypeFromSchema(s, false));

                    foreach (KeyValuePair<string, JToken> jp in jo)
                    {
                        map.Add(jp.Key, GetDefaultValue((s as MapSchema).ValueSchema, jp.Value));
                    }

                    return map;

                case Schema.Type.Union:
                    return GetDefaultValue((s as UnionSchema).Schemas[0], defaultValue);

                default:
                    throw new AvroException($"Unsupported schema type {s.Tag}");
            }
        }

        /// <summary>
        /// Deserializes a enum. Uses CreateEnum to construct the new enum object.
        /// </summary>
        /// <param name="reuse">If appropirate, uses this instead of creating a new enum object.</param>
        /// <param name="writerSchema">The schema the writer used while writing the enum</param>
        /// <param name="readerSchema">The schema the reader is using</param>
        /// <param name="d">The decoder for deserialization.</param>
        /// <returns>An enum object.</returns>
        protected override object ReadEnum(object reuse, EnumSchema writerSchema, Schema readerSchema, Decoder d)
        {
            var i = d.ReadEnum();
            var symbol = writerSchema[i];
            var es = readerSchema as EnumSchema;
            var enumType = EnumCache.GetEnumeration(es);
            return Enum.Parse(enumType, symbol);
        }

        /// <summary>
        /// Deserializes a record from the stream.
        /// </summary>
        /// <param name="reuse">If not null, a record object that could be reused for returning the result</param>
        /// <param name="writerSchema">The writer's RecordSchema</param>
        /// <param name="readerSchema">The reader's schema, must be RecordSchema too.</param>
        /// <param name="dec">The decoder for deserialization</param>
        /// <returns>The record object just read</returns>
        protected override object ReadRecord(object reuse, RecordSchema writerSchema, Schema readerSchema, Decoder dec)
        {
            RecordSchema rs = (RecordSchema)readerSchema;

            object rec = reuse;
            if (rec == null)
            {
                rec = RecordFactory(GetTypeFromSchema(rs, false));
                if (rec == null)
                {
                    throw new Exception($"Couldn't create type matching schema name {rs.Fullname}");
                }
            }

            object obj;
            foreach (Field wf in writerSchema)
            {
                try
                {
                    Field rf;
                    if (rs.TryGetField(wf.Name, out rf))
                    {
                        obj = ClassCache.GetClass(writerSchema).GetValue(rec, rf);
                        ClassCache.GetClass(writerSchema).SetValue(rec, rf, Read(obj, wf.Schema, rf.Schema, dec));
                    }
                    else
                    {
                        Skip(wf.Schema, dec);
                    }
                }
                catch (Exception ex)
                {
                    throw new AvroException(ex.Message + " in field " + wf.Name, ex);
                }
            }

            foreach (Field rf in rs)
            {
                if (writerSchema.Contains(rf.Name))
                {
                    continue;
                }

                ClassCache.GetClass(rs).SetValue(rec, rf, GetDefaultValue(rf.Schema, rf.DefaultValue));
            }

            return rec;
        }

        /// <summary>
        /// Deserializes a fixed object and returns the object. The default implementation uses CreateFixed()
        /// and GetFixedBuffer() and returns what CreateFixed() returned.
        /// </summary>
        /// <param name="reuse">If appropriate, uses this object instead of creating a new one.</param>
        /// <param name="writerSchema">The FixedSchema the writer used during serialization.</param>
        /// <param name="readerSchema">The schema that the readr uses. Must be a FixedSchema with the same
        /// size as the writerSchema.</param>
        /// <param name="d">The decoder for deserialization.</param>
        /// <returns>The deserilized object.</returns>
        protected override object ReadFixed(object reuse, FixedSchema writerSchema, Schema readerSchema, Decoder d)
        {
            FixedSchema rs = readerSchema as FixedSchema;
            if (rs.Size != writerSchema.Size)
            {
                throw new AvroException($"Size mismatch between reader and writer fixed schemas. Writer: {writerSchema}, reader: {readerSchema}");
            }

            byte[] fixedrec = new byte[rs.Size];
            d.ReadFixed(fixedrec);
            return fixedrec;
        }


        /// <summary>
        /// Reads an array from the given decoder
        /// </summary>
        /// <param name="reuse">object to store data read</param>
        /// <param name="writerSchema">schema of the object that wrote the data</param>
        /// <param name="readerSchema">schema of the object that will store the data</param>
        /// <param name="dec">decoder object that contains the data to be read</param>
        /// <returns>array</returns>
        protected override object ReadArray(object reuse, ArraySchema writerSchema, Schema readerSchema, Decoder dec)
        {
            ArraySchema rs = readerSchema as ArraySchema;
            System.Collections.IList array;
            if (reuse != null)
            {
                array = reuse as System.Collections.IList;
                if (array == null)
                    throw new AvroException("array object does not implement non-generic IList");
                array.Clear();
            }
            else
            {
                array = (System.Collections.IList)Activator.CreateInstance(GetTypeFromSchema(rs, false));
            }

            int i = 0;
            for (int n = (int)dec.ReadArrayStart(); n != 0; n = (int)dec.ReadArrayNext())
            {
                for (int j = 0; j < n; j++, i++)
                    array.Add(Read(null, writerSchema.ItemSchema, rs.ItemSchema, dec));
            }

            return array;
        }

        /// <summary>
        /// Deserialized an avro map. The default implemenation creats a new map using CreateMap() and then
        /// adds elements to the map using AddMapEntry().
        /// </summary>
        /// <param name="reuse">If appropriate, use this instead of creating a new map object.</param>
        /// <param name="writerSchema">The schema the writer used to write the map.</param>
        /// <param name="readerSchema">The schema the reader is using.</param>
        /// <param name="d">The decoder for serialization.</param>
        /// <returns>The deserialized map object.</returns>
        protected override object ReadMap(object reuse, MapSchema writerSchema, Schema readerSchema, Decoder d)
        {
            MapSchema rs = readerSchema as MapSchema;
            System.Collections.IDictionary map;
            if (reuse != null)
            {
                map = reuse as System.Collections.IDictionary;
                if (map == null)
                    throw new AvroException("map object does not implement IDictionary");

                map.Clear();
            }
            else
            {
                map = (System.Collections.IDictionary)Activator.CreateInstance(GetTypeFromSchema(rs, false));
            }

            for (int n = (int)d.ReadMapStart(); n != 0; n = (int)d.ReadMapNext())
            {
                for (int j = 0; j < n; j++)
                {
                    string k = d.ReadString();
                    map[k] = Read(null, writerSchema.ValueSchema, rs.ValueSchema, d);   // always create new map item
                }
            }

            return map;
        }
    }
}
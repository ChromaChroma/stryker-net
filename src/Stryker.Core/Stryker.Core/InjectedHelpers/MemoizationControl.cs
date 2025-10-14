#define TRACK_STEPS


using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

#if NETFRAMEWORK
using Newtonsoft.Json;
#elif NETCOREAPP3_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace Stryker
{
    public static class MemoizationControl
    {
        private static readonly MmfLinkedListStringDictionary IsSerializableTypeDict = new("IsSerializableDictionary");

        private static readonly MmfLinkedListStringDictionary MemoizationDict = new("SerializedMemoizationDictionary");


        private static readonly System.Collections.Generic.Dictionary<string, string> IsSerializableTypeDict2 = new();
        private static readonly System.Collections.Generic.Dictionary<string, string> MemoizationDict2 = new();

#if NETFRAMEWORK
     private static readonly JsonSerializerSettings Settings = new()
        {
            Formatting = Formatting.Indented,
            Converters = { new AllFieldsConverter() } // custom converter
        };
#elif NETCOREAPP3_0_OR_GREATER
        private static readonly JsonSerializerOptions Options =
            new() { WriteIndented = true, Converters = { new AllFieldsConverterFactory() } };
#endif


        // this attribute will be set by the Stryker Data Collector before each test
        public static bool CaptureCoverage;

        //====

        public static bool CaptureMemoizationHitsAndMisses;

        private static System.Collections.Generic.List<(string, bool, long, long, long, long, long, long, string)>
            _memoizationData = new()
            {
                // ("SomeFunc(X, Y, Z)", true, 0.012345d),
                // ("SomeFuncOther(X, Y, Z)", false, -1.0d),
            };

        // Returns the logged memoiation measurement entries. Afterwards clears the list for future calls
        public static System.Collections.Generic.IList<(string, bool, long, long, long, long, long, long, string)>[]
            GetMemoizationData()
        {
            var result =
                new System.Collections.Generic.IList<(string, bool, long, long, long, long, long, long, string)>[]
                {
                    _memoizationData
                };
            ResetMemoizationInfo();
            return result;
        }

        public static void ResetMemoizationInfo() => _memoizationData = new();


        //====

        static MemoizationControl()
        {
            var currentNamespace = typeof(MemoizationControl).Namespace;
            var assembly = typeof(MemoizationControl).Assembly;
            var mutantControlType = assembly.GetType($"{currentNamespace}.MutantControl");
            if (mutantControlType != null)
            {
                var captureCoverageField = mutantControlType.GetField("CaptureCoverage");
                if (captureCoverageField != null)
                {
                    CaptureCoverage = (bool)captureCoverageField.GetValue(null)!; // static field, so null instance
                }
            }

            // while (!Debugger.IsAttached)
            // {
            //     Thread.Sleep(100);
            // }
            // Debugger.Break();
        }

        // check with: Stryker.MemoizationControl.RetrieveMemoization<T>(ID, FUNC, PRED)
        public static T RetrieveMemoization<T>(string id, Func<T> func, Func<bool> predicate = null)
        {
            // return func();
            // If mutant active predicate is true, compute original code, do not memoize
            if (predicate != null && predicate())
            {
                return func();
            }

#if TRACK_STEPS
            try
            {
                long timeTotal = 0,
                    timeToCheckSerializibility = -1,
                    timeToTryGetValue = -1,
                    timeToDeserialize = -1,
                    timeToSerialize = -1,
                    timeToStore = -1;

                var sw = new Stopwatch();

                sw.Start();
                var serializabilityStored = IsSerializableTypeDict
                    .TryGetValue(typeof(T).FullName, out string isSerializable);
                var typeIsSerializable = !(typeof(T).IsInterface || typeof(T).IsAbstract) && serializabilityStored &&
                                         bool.Parse(isSerializable);


                sw.Stop();
                timeToCheckSerializibility = sw.ElapsedTicks;
                timeTotal += timeToCheckSerializibility;

                if (typeIsSerializable)
                {
                    sw.Restart();
                    bool foundValue = MemoizationDict.TryGetValue(id, out var value);
                    sw.Stop();
                    timeToTryGetValue = sw.ElapsedTicks;
                    timeTotal += timeToTryGetValue;

                    if (foundValue)
                    {
                        sw.Restart();
                        T? v = DoDeserialize<T>(value);
                        sw.Stop();
                        timeToDeserialize = sw.ElapsedTicks;
                        timeTotal += timeToDeserialize;

                        if (v != null)
                        {
                            T validationResult = func.Invoke();


                            // var isInterface = typeof(T).IsInterface || typeof(T).IsAbstract;
                            var isIEnumerable =
                                typeof(T).GetInterfaces()
                                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                            if ((isIEnumerable && ((IEnumerable<T>)v).SequenceEqual((IEnumerable<T>)validationResult))
                                || v.Equals(validationResult))
                            {
                                _memoizationData.Add((
                                    id,
                                    true,
                                    timeTotal,
                                    timeToCheckSerializibility,
                                    timeToTryGetValue,
                                    timeToDeserialize,
                                    timeToSerialize,
                                    timeToStore,
                                    ""
                                ));
                                return v;
                            }
                            //
                            // if (v.Equals(validationResult))
                            // {
                            //     _memoizationData.Add((
                            //         id,
                            //         true,
                            //         timeTotal,
                            //         timeToCheckSerializibility,
                            //         timeToTryGetValue,
                            //         timeToDeserialize,
                            //         timeToSerialize,
                            //         timeToStore,
                            //         ""
                            //     ));
                            //     return v;
                            // }
                            else
                            {
                                _memoizationData.Add((
                                    id,
                                    true,
                                    timeTotal,
                                    timeToCheckSerializibility,
                                    timeToTryGetValue,
                                    timeToDeserialize,
                                    timeToSerialize,
                                    timeToStore,
                                    $"Memoized Value not same as expected Computed Value. Memoization might not be possible. Memoized value: {v}. Computed value: {validationResult}"
                                ));
                                // Memoization not same value as expected
                                return validationResult;
                            }
                        }
                        else
                        {
                            // Catch possible multiple invoke and insets below if false ^
                        }
                    }
                }

                T result = func.Invoke();
                // Assuming null is valid result, no assumption of invocated logic.
                // then check if type is serializable (but maybe not in memoization). If not yet checked, Check if serializable
                if (result == null || !serializabilityStored || typeIsSerializable)
                {
                    sw.Restart();
                    var isSuccessfull = TrySerialize(result, out string serializedJson);
                    sw.Stop();
                    timeToSerialize = sw.ElapsedTicks;
                    timeTotal += timeToSerialize;

                    if (isSuccessfull)
                    {
                        sw.Restart();
                        MemoizationDict.Add(id, serializedJson);
                        sw.Stop();
                        timeToStore = sw.ElapsedTicks;
                        timeTotal += timeToStore;
                    }
                }

                _memoizationData.Add((
                    id,
                    false,
                    timeTotal,
                    timeToCheckSerializibility,
                    timeToTryGetValue,
                    timeToDeserialize,
                    timeToSerialize,
                    timeToStore,
                    ""
                ));
                return result;
            }
            catch (Exception ex)
            {
                _memoizationData.Add((
                    id,
                    false,
                    -1,
                    -1,
                    -1,
                    -1,
                    -1,
                    -1,
                    ex.ToString()
                ));
                return func();
            }
#else
            var serializabilityStored =
                IsSerializableTypeDict
                    // IsSerializableTypeDict2
                    .TryGetValue(typeof(T).FullName!, out string isSerializable);
            var typeIsSerializable = serializabilityStored && bool.Parse(isSerializable);

            if (typeIsSerializable)
            {
                bool foundValue = MemoizationDict.TryGetValue(id, out var value);


                if (foundValue)
                {
                    T? v = DoDeserialize<T>(value);

                    if (v != null)
                    {
                        return v;
                    }
                    else
                    {
                        // Catch possible multiple invoke and insets below if false ^
                    }
                }
            }

            T result = func.Invoke();
            // Assuming null is valid result, no assumption of invocated logic.
            // then check if type is serializable (but maybe not in memoization). If not yet checked, Check if serializable
            if (result == null || !serializabilityStored || typeIsSerializable)
            {
                var isSuccessfull = TrySerialize(result, out string serializedJson);

                if (isSuccessfull)
                {
                    MemoizationDict.Add(id, serializedJson);
                    // MemoizationDict2.Add(id, serializedJson);
                }
            }

            return result;
#endif
        }

        private static T DoDeserialize<T>(string value)
        {
#if NETFRAMEWORK
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(value, Settings);
#elif NETCOREAPP3_0_OR_GREATER
                return JsonSerializer.Deserialize<T>(value, Options);
#else
                #error Unsupported target framework. Please compile for .NET Framework or .NET Core 3.0+
#endif
        }
        public static string DoSerialize(object obj)
        {
#if NETFRAMEWORK
            return JsonConvert.SerializeObject(value, obj?.GetType(), Settings);
#elif NETCOREAPP3_0_OR_GREATER
            throw new ArgumentException("");
            return JsonSerializer.Serialize(obj, obj?.GetType(), Options);
#else
                #error Unsupported target framework. Please compile for .NET Framework or .NET Core 3.0+
#endif
        }
        // check with: Stryker.MemoizationControl.GenerateMemoizationId(ID, PARAMS)
        public static string GenerateMemoizationId(string methodIdentifier, params object[] args)
            => methodIdentifier + "†" + string.Join(
                "‡",
                GetSerializableArgs(args)
            );

        private static System.Collections.Generic.IEnumerable<string> GetSerializableArgs(object[] args)
        {
            System.Collections.Generic.List<string> acc = new();
            foreach (var arg in args)
            {
                if (arg == null)
                {
                    acc.Add("null");
                }
                else if (arg is string || arg.GetType().IsPrimitive)
                {
                    acc.Add(arg.ToString());
                }
                else
                {
                    if (TrySerialize(arg, out var serializedJson))
                    {
                        // JsonSerializer only serializes non cyclic an public fields
                        acc.Add(serializedJson);
                    }
                    // Else ignoreOR return a placeholder "non-serializable"? This at least keeps order of args.
                    else
                    {
                        acc.Add("|non-serializable|");
                    }
                }
            }

            return acc;
        }

        private static bool TrySerialize(object obj, out string serializedJson)
        {
            serializedJson = null;

            var serializabilityFound =
                IsSerializableTypeDict.TryGetValue(obj.GetType().FullName, out string isSerializable);
            if (serializabilityFound)
            {
                var serializable = bool.Parse(isSerializable);
                if (serializable)
                {
                    try
                    {
                        serializedJson = DoSerialize(obj);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                return false;
            }

            try
            {
                serializedJson = DoSerialize(obj);
                IsSerializableTypeDict.Add(obj.GetType().FullName, true.ToString());
                return true;
            }
            catch
            {
                IsSerializableTypeDict.Add(obj.GetType().FullName, false.ToString());
                return false;
            }

            //
            // serializedJson = null;
            // if (!IsSerializableTypeDict.TryGetValue(obj.GetType().FullName, out string isSerializable))
            // {
            //     try
            //     {
            //         serializedJson = JsonSerializer.Serialize(obj, Options);
            //         IsSerializableTypeDict.Add(obj.GetType().FullName, true.ToString());
            //         isSerializable = true.ToString();
            //     }
            //     catch
            //     {
            //         IsSerializableTypeDict.Add(obj.GetType().FullName, false.ToString());
            //         isSerializable = false.ToString();
            //     }
            // }
            //
            //
            // return bool.Parse(isSerializable);
        }


        // // check with: Stryker.MemoizationControl.GenerateMemoizationId(ID, PARAMS)
        // public static string GenerateMemoizationId(string methodIdentifier, params object[] args) => $"{methodIdentifier}__{ToHash(args)}";
        //
        // private static string ToHash<T>(IEnumerable<T> items)
        // {
        //     var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
        //     var bytes = Encoding.UTF8.GetBytes(json);
        //     var hash = SHA256.HashData(bytes);
        //     return Convert.ToBase64String(hash);
        // }
        //


        // // check with: Stryker.MemoizationControl.GetMemoization<T>(ID)
        // public static T GetMemoization<T>(string id)
        // {
        //     return default(T);
        // }

        // check with: Stryker.MemoizationControl.StoreMemoization<T>(ID, VALUE)
        // public static T StoreMemoization<T>(string id, T value)
        // {
        //     return value;
        // }
    }

    #region JsonConverter

#if NETFRAMEWORK
public class AllFieldsConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) =>
        objectType.IsClass && objectType.GetConstructor(Type.EmptyTypes) != null;

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var obj = Activator.CreateInstance(objectType);
        var fields = objectType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        var jObject = Newtonsoft.Json.Linq.JObject.Load(reader);
        foreach (var field in fields)
        {
            if (jObject.TryGetValue(field.Name, out var token))
            {
                var fieldValue = token.ToObject(field.FieldType, serializer);
                field.SetValue(obj, fieldValue);
            }
        }

        return obj;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        var fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields)
        {
            writer.WritePropertyName(field.Name);
            serializer.Serialize(writer, field.GetValue(value));
        }
        writer.WriteEndObject();
    }
}
#elif NETCOREAPP3_0_OR_GREATER
    public class AllFieldsConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            // Only handle reference types with parameterless constructors
            return typeToConvert.IsClass && typeToConvert.GetConstructor(Type.EmptyTypes) != null;
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            // Create the generic converter type
            var converterType = typeof(AllFieldsConverter<>).MakeGenericType(typeToConvert);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
    }

    public class AllFieldsConverter<T> : JsonConverter<T> where T : new()
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            var obj = new T();
            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return obj;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string fieldName = reader.GetString();
                    reader.Read();

                    var field = Array.Find(fields, f => f.Name == fieldName);
                    if (field != null)
                    {
                        object value = JsonSerializer.Deserialize(ref reader, field.FieldType, options);
                        field.SetValue(obj, value);
                    }
                    else
                    {
                        reader.Skip(); // skip unknown fields
                    }
                }
            }

            throw new JsonException("Incomplete JSON object");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                writer.WritePropertyName(field.Name);
                var fieldValue = field.GetValue(value);
                JsonSerializer.Serialize(writer, fieldValue, field.FieldType, options);
            }

            writer.WriteEndObject();
        }
    }

#endif
    #endregion

    public class MutexLock : IDisposable
    {
        private readonly Mutex _mutex;

        public MutexLock(Mutex mutex)
        {
            _mutex = mutex ?? throw new ArgumentNullException(nameof(mutex));
            _mutex.WaitOne();
        }

        public void Dispose()
        {
            _mutex.ReleaseMutex();
        }
    }

    public class MmfLinkedListStringDictionary : MmfLinkedListStringDictionaryBase,
        System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>>
    {
        private const int DefaultByteDataCapacity = 1024 * 1024;

        public MmfLinkedListStringDictionary(string name,
            MemoryMappedFileAccess access = MemoryMappedFileAccess.ReadWrite,
            string memFile = "Memoization.data",
            string memPath = @"c:\.StrykData",
            int byteDataCapacity = DefaultByteDataCapacity)
            : base(name, access, memFile, memPath, byteDataCapacity)
        {
        }

        public void Add(string key, string value)
        {
            // var sw = new Stopwatch();
            // sw.Start();
            using (new MutexLock(Mutex)) ;
            // Console.WriteLine($"Adding (Aquiring Mutex): {sw.ElapsedMilliseconds}ms");
            AddInternal(key, value);
            // Console.WriteLine($"Adding (Done): {sw.ElapsedMilliseconds}ms");
        }

        public bool TryGetValue(string key, out string value)
        {
            using (new MutexLock(Mutex)) ;
            return TryGetValueInternal(key, out value);
        }

        public new long GetBytesUsed()
        {
            using (new MutexLock(Mutex)) ;
            return base.GetBytesUsed();
        }

        public Mutex GetMutex()
        {
            return Mutex;
        }

        public System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<string, string>>
            GetEnumerator()
        {
            return GetEnumeratorInternal();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public abstract class VersionManagedMmfDataStructure
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ControlMapHeaderMinimal
        {
            public uint Magic; // sanity check
            public int CurrentMapId; // active MMF id
            public int Version; // optional: can help clients detect updates
            public int DataCapacity; // optional: can help clients detect updates
        }

        private readonly string _baseName; // base name of normal MMF
        private readonly int _baseDataCapacity;
        public readonly MemoryMappedFile _controlMmf;
        public readonly MemoryMappedViewAccessor _controlAccessor;

        protected VersionManagedMmfDataStructure(string baseName, int baseDataCapacity)
        {
            _baseName = baseName;
            _baseDataCapacity = baseDataCapacity;
            _controlMmf = MemoryMappedFile.CreateOrOpen(
                $"{_baseName}_Control",
                Marshal.SizeOf<ControlMapHeaderMinimal>(),
                MemoryMappedFileAccess.ReadWrite
            );
            _controlAccessor = _controlMmf.CreateViewAccessor(0, Marshal.SizeOf<ControlMapHeaderMinimal>(),
                MemoryMappedFileAccess.ReadWrite);

            _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
            InitializeIfNeeded(header);
        }

        private void InitializeIfNeeded(ControlMapHeaderMinimal header)
        {
            if (header.Magic != 0xCAFEBABE)
            {
                // Initialize the control map if it's not already initialized
                header.Magic = 0xCAFEBABE;
                header.CurrentMapId = 1; // Start with map ID 1
                header.Version = 1;
                header.DataCapacity = _baseDataCapacity;
                _controlAccessor.Write(0, ref header);
            }
        }

        protected int GetNewestVersion()
        {
            _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
            return header.Version;
        }

        protected int GetNewestDataCapacity()
        {
            _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
            return header.DataCapacity;
        }

        protected string GetNewestVersionMmfName()
        {
            _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
            return $"{_baseName}_{header.Version}";
        }

        protected int UpdateToNewVersion(int newDataCapacity)
        {
            _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
            if (newDataCapacity < header.DataCapacity)
            {
                throw new ArgumentException("INTERNAL: currently now allowed to decrease size of MMF");
            }

            header.DataCapacity *= 2;
            int newVersion = header.Version + 1;
            header.Version = newVersion;
            _controlAccessor.Write(0, ref header);
            return newVersion;
        }

        /// Call this method to validate if the version of the underlaying MMF is current. If not, update the MMF.
        protected abstract void ValidateVersionOfMmf();
    }

    public class MmfLinkedListStringDictionaryBase : VersionManagedMmfDataStructure
    {
        public static readonly int[] Primes =
        [
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
        ];

        [StructLayout(LayoutKind.Sequential)]
        private struct Header
        {
            public uint Magic; // e.g. 0xCAFEBABE
            public int version;
            public long BytesUsed;
            [MarshalAs(UnmanagedType.I1)] public bool IsCurrent;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EntryHeader
        {
            public int Index;
            public int Next; // 0 will work as end of list as 0 offset would be in the header, so we can safely use it.
            public int SizeOfKey;
            public int SizeOfValue;

            // Not in header due to variable size
            // public string Key;
            // public string Value;
        }
        //
        // private const int DefaultByteDataCapacity = 1024 * 1024; // 1000 * 4 entries, assuming simple 4 byte TKey and TValue

        // Offsets within each entry
        private const int IndexOffset = 0;
        private const int NextOffset = 4;
        private const int SizeOfKeyOffset = 8;
        private const int SizeOfValueOffset = 12;
        private const int KeyOffset = 16;
        private readonly int _headerSize = Marshal.SizeOf<Header>();
        private readonly int _entryHeaderSize = Marshal.SizeOf<EntryHeader>();

        // Backing Data Structure objects
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;

        protected readonly Mutex Mutex;
        // private readonly EventWaitHandle _ewh;
        // private readonly RegisteredWaitHandle _rwh;

        // Configuration fields
        private readonly MemoryMappedFileAccess _mmfAccess;
        private readonly string _mmfName;

        protected MmfLinkedListStringDictionaryBase(
            string name,
            MemoryMappedFileAccess access,
            string memFile,
            string memPath,
            int byteDataCapacity)
            : base(name, byteDataCapacity)
        {
            _mmfName = name;
            _mmfAccess = access;

            string filePath = $@"{memPath}\{memFile}";

            using (FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                       FileShare.ReadWrite))
            {
                fs.SetLength(byteDataCapacity); // sets file size
                _mmf = MemoryMappedFile.CreateFromFile(fs, null, byteDataCapacity, MemoryMappedFileAccess.ReadWrite,
                    HandleInheritability.None, true);
            }
            // _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.OpenOrCreate, name);

            _accessor = _mmf.CreateViewAccessor();
            Mutex = new Mutex(false, $"{name}Mutex", out bool createdNew);

            if (createdNew)
            {
                // If Mmf is newly made, initialize its header
                Header header = new Header { Magic = 0xCAFEBABE, version = 1, IsCurrent = true };
                _accessor.Write(0, ref header);
            }
        }

        protected override void ValidateVersionOfMmf()
        {
        }

        // private void Resize()
        // {
        // }
        protected long GetCapacity()
        {
            return _accessor.Capacity;
        }

        protected long GetBytesUsed()
        {
            _accessor.Read(0, out Header header);
            return header.BytesUsed;
        }

        protected void AddInternal(string key, string value)
        {
            // ValidateVersionOfMmf();
            if (!(_accessor.CanRead || _accessor.CanWrite))
                throw new InvalidOperationException(
                    "Internal: MMF accessor is does not have the required Read/Write permissions.");
            int currentPositionOffset = _headerSize;

            while (true)
            {
                // LOGIC IF WE HANDLE DUPLICATE INSERTS/ADDS OF KEYS
                // int keySize = _accessor.ReadInt32(currentPositionOffset + SizeOfKeyOffset);
                // byte[] keyBytes = new byte[keySize];
                // _accessor.ReadArray(currentPositionOffset + KeyOffset, keyBytes, 0, keySize);
                // string entryKey = System.Text.Encoding.Default.GetString(keyBytes);
                // if (key == entryKey)
                // {
                //     // TODO decide:
                //     //  Overwrite, Overwrite is logical, but cannot in current array structure.
                //     //  ignore,
                //     //  throw exception? Since we dont expect an add, if key exists and is retrieved.
                //     throw new ArgumentException("INTERNAL: An item with the same key has already been added.");
                // }

                _accessor.Read(currentPositionOffset, out EntryHeader eh);

                if (eh.Next is not 0) //not end of list
                {
                    currentPositionOffset = eh.Next;
                    continue;
                }

                int newEntryPosition = currentPositionOffset;
                if (eh.SizeOfKey != 0) // Check to determine if this is the first entry
                {
                    newEntryPosition += KeyOffset + eh.SizeOfKey + eh.SizeOfKey;
                }

                byte[] newKeyBytes = System.Text.Encoding.UTF8.GetBytes(key);
                byte[] newValueBytes = System.Text.Encoding.UTF8.GetBytes(value);

                if (newEntryPosition + _entryHeaderSize + newKeyBytes.Length + newValueBytes.Length >
                    _accessor.Capacity)
                {
                    // Resize if Size of new entry exceeds current capacity of bytes
                    throw new IndexOutOfRangeException("Backing MMF is too small");
                    // Resize();
                }

                //Write new entry
                _accessor.Write(newEntryPosition + NextOffset, 0);
                _accessor.Write(newEntryPosition + SizeOfKeyOffset, newKeyBytes.Length);
                _accessor.Write(newEntryPosition + SizeOfValueOffset, newValueBytes.Length);
                _accessor.WriteArray(newEntryPosition + KeyOffset, newKeyBytes, 0, newKeyBytes.Length);
                _accessor.WriteArray(newEntryPosition + KeyOffset + newKeyBytes.Length, newValueBytes, 0,
                    newValueBytes.Length);

                if (eh.SizeOfKey != 0)
                {
                    // update previous entry's next value
                    _accessor.Write(currentPositionOffset + NextOffset, newEntryPosition);
                }

                _accessor.Read(0, out Header header);
                header.BytesUsed =
                    newEntryPosition + KeyOffset + newKeyBytes.Length + newValueBytes.Length; // End of inserted entry
                _accessor.Write(0, ref header);


                return;
            }
        }

        protected bool TryGetValueInternal(string key, out string value)
        {
            // ValidateVersionOfMmf();
            if (!(_accessor.CanRead || _accessor.CanWrite))
                throw new InvalidOperationException(
                    "Internal: MMF accessor is does not have the required Read/Write permissions.");

            int currentPositionOffset = _headerSize;
            while (true)
            {
                _accessor.Read(currentPositionOffset, out EntryHeader eh);

                byte[] keyBytes = new byte[eh.SizeOfKey];
                _accessor.ReadArray(currentPositionOffset + KeyOffset, keyBytes, 0, eh.SizeOfKey);
                string entryKey = System.Text.Encoding.Default.GetString(keyBytes);

                if (key == entryKey)
                {
                    byte[] valueBytes = new byte[eh.SizeOfValue];
                    _accessor.ReadArray(currentPositionOffset + KeyOffset + eh.SizeOfKey, valueBytes, 0,
                        eh.SizeOfValue);
                    value = System.Text.Encoding.Default.GetString(valueBytes);
                    return true;
                }

                if (eh.Next is 0) // End of list
                {
                    break;
                }

                currentPositionOffset = eh.Next;
            }

            value = null;
            return false;
        }

        public System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<string, string>>
            GetEnumeratorInternal()
        {
            // ValidateVersionOfMmf();

            if (!(_accessor.CanRead || _accessor.CanWrite))
                throw new InvalidOperationException(
                    "Internal: MMF accessor does not have the required Read/Write permissions.");

            int currentPositionOffset = _headerSize;

            while (true)
            {
                _accessor.Read(currentPositionOffset, out EntryHeader eh);

                // Read key (but discard it, since we only want values)
                byte[] keyBytes = new byte[eh.SizeOfKey];
                _accessor.ReadArray(currentPositionOffset + KeyOffset, keyBytes, 0, eh.SizeOfKey);
                string entryKey = System.Text.Encoding.Default.GetString(keyBytes);

                // Read value
                byte[] valueBytes = new byte[eh.SizeOfValue];
                _accessor.ReadArray(currentPositionOffset + KeyOffset + eh.SizeOfKey, valueBytes, 0, eh.SizeOfValue);
                var value = System.Text.Encoding.Default.GetString(valueBytes);

                yield return new System.Collections.Generic.KeyValuePair<string, string>(entryKey, value);

                if (eh.Next <= 0) // End of list
                    yield break;

                currentPositionOffset = eh.Next;
            }
        }
    }
}

// using System;
// using System.Collections;
// using System.Collections.Generic;
// using System.Diagnostics;
// using System.IO;
// using System.IO.MemoryMappedFiles;
// using System.Runtime.InteropServices;
// using System.Text;
// using System.Threading;
//
// namespace Stryker.TestRunner.VsTest;
//
//
//     public class MutexLock : IDisposable
//     {
//         private readonly Mutex _mutex;
//
//         public MutexLock(Mutex mutex)
//         {
//             _mutex = mutex ?? throw new ArgumentNullException(nameof(mutex));
//             _mutex.WaitOne();
//         }
//
//         public void Dispose()
//         {
//             _mutex.ReleaseMutex();
//         }
//     }
//
//     public abstract class VersionManagedMmfDataStructure
//     {
//         [StructLayout(LayoutKind.Sequential)]
//         private struct ControlMapHeaderMinimal
//         {
//             public uint Magic; // sanity check
//             public int CurrentMapId; // active MMF id
//             public int Version; // optional: can help clients detect updates
//             public int DataCapacity; // optional: can help clients detect updates
//         }
//
//         private readonly string _baseName; // base name of normal MMF
//         private readonly int _baseDataCapacity;
//         private readonly MemoryMappedFile _controlMmf;
//         private readonly MemoryMappedViewAccessor _controlAccessor;
//
//         protected VersionManagedMmfDataStructure(string baseName, int baseDataCapacity)
//         {
//             _baseName = baseName;
//             _baseDataCapacity = baseDataCapacity;
//             _controlMmf = MemoryMappedFile.CreateOrOpen(
//                 $"{_baseName}_Control",
//                 Marshal.SizeOf<ControlMapHeaderMinimal>(),
//                 MemoryMappedFileAccess.ReadWrite
//             );
//             _controlAccessor = _controlMmf.CreateViewAccessor(0, Marshal.SizeOf<ControlMapHeaderMinimal>(),
//                 MemoryMappedFileAccess.ReadWrite);
//
//             _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
//             InitializeIfNeeded(header);
//         }
//
//         private void InitializeIfNeeded(ControlMapHeaderMinimal header)
//         {
//             if (header.Magic != 0xCAFEBABE)
//             {
//                 // Initialize the control map if it's not already initialized
//                 header.Magic = 0xCAFEBABE;
//                 header.CurrentMapId = 1; // Start with map ID 1
//                 header.Version = 1;
//                 header.DataCapacity = _baseDataCapacity;
//                 _controlAccessor.Write(0, ref header);
//             }
//         }
//
//         protected int GetNewestVersion()
//         {
//             _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
//             return header.Version;
//         }
//
//         protected int GetNewestDataCapacity()
//         {
//             _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
//             return header.DataCapacity;
//         }
//
//         protected string GetNewestVersionMmfName()
//         {
//             _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
//             return $"{_baseName}_{header.Version}";
//         }
//
//         protected int UpdateToNewVersion(int newDataCapacity)
//         {
//             _controlAccessor.Read(0, out ControlMapHeaderMinimal header);
//             if (newDataCapacity < header.DataCapacity)
//             {
//                 throw new ArgumentException("INTERNAL: currently now allowed to decrease size of MMF");
//             }
//
//             header.DataCapacity *= 2;
//             int newVersion = header.Version + 1;
//             header.Version = newVersion;
//             _controlAccessor.Write(0, ref header);
//             return newVersion;
//         }
//
//         /// Call this method to validate if the version of the underlaying MMF is current. If not, update the MMF.
//         protected abstract void ValidateVersionOfMmf();
//     }
//
//     public class MmfLinkedListStringDictionaryBase : VersionManagedMmfDataStructure
//     {
//         public static readonly int[] Primes =
//         [
//             3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
//             1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
//             17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
//             187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
//             1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
//         ];
//
//         [StructLayout(LayoutKind.Sequential)]
//         private struct Header
//         {
//             public uint Magic; // e.g. 0xCAFEBABE
//             public int version;
//             [MarshalAs(UnmanagedType.I1)] public bool IsCurrent;
//         }
//
//         [StructLayout(LayoutKind.Sequential)]
//         private struct EntryHeader
//         {
//             public int Index;
//             public int Next; // 0 will work as end of list as 0 offset would be in the header, so we can safely use it.
//             public int SizeOfKey;
//             public int SizeOfValue;
//
//             // Not in header due to variable size
//             // public string Key;
//             // public string Value;
//         }
//
//         private const int DefaultByteDataCapacity = 50; // 1000 * 4 entries, assuming simple 4 byte TKey and TValue
//
//         // Offsets within each entry
//         private const int IndexOffset = 0;
//         private const int NextOffset = 4;
//         private const int SizeOfKeyOffset = 8;
//         private const int SizeOfValueOffset = 12;
//         private const int KeyOffset = 16;
//         private readonly int _headerSize = Marshal.SizeOf<Header>();
//         private readonly int _entryHeaderSize = Marshal.SizeOf<EntryHeader>();
//
//         // Backing Data Structure objects
//         private MemoryMappedFile _mmf;
//         private MemoryMappedViewAccessor _accessor;
//         protected readonly Mutex Mutex;
//         private readonly EventWaitHandle _ewh;
//         private readonly RegisteredWaitHandle _rwh;
//
//         // Configuration fields
//         private readonly MemoryMappedFileAccess _mmfAccess;
//         private readonly string _mmfName;
//
//         protected MmfLinkedListStringDictionaryBase(
//             string name,
//             MemoryMappedFileAccess access,
//             int byteDataCapacity = DefaultByteDataCapacity)
//             : base(name, byteDataCapacity)
//         {
//             _mmfName = name;
//             _mmfAccess = access;
//
//             try
//             {
//                 _mmf = MemoryMappedFile.OpenExisting(GetNewestVersionMmfName(), MemoryMappedFileRights.ReadWrite);
//             }
//             catch (FileNotFoundException)
//             {
//                 _mmf = MemoryMappedFile.CreateNew(GetNewestVersionMmfName(), GetNewestDataCapacity() + _headerSize,
//                     access);
//             }
//
//             _accessor = _mmf.CreateViewAccessor(0, GetNewestDataCapacity() + _headerSize);
//             Mutex = new Mutex(false, $"{name}Mutex", out bool createdNew);
//
//             if (createdNew)
//             {
//                 // If Mmf is newly made, initialize its header
//                 Header header = new Header { Magic = 0xCAFEBABE, version = 1, IsCurrent = true };
//                 _accessor.Write(0, ref header);
//             }
//
//             _ewh = new EventWaitHandle(false,　EventResetMode.AutoReset,　$"{name}_EventWaitHandle");
//             _rwh = ThreadPool.RegisterWaitForSingleObject(_ewh, (_, _) => ValidateVersionOfMmf(), null, -1, false);
//         }
//
//         protected override void ValidateVersionOfMmf()
//         {
//             _accessor.Read(0, out Header header);
//             if (header.IsCurrent)
//             {
//                 return;
//             }
//
//             try
//             {
//                 var newMmf = MemoryMappedFile.OpenExisting(
//                     GetNewestVersionMmfName(),
//                     MemoryMappedFileRights.ReadWrite
//                 );
//                 var newAccessor = newMmf.CreateViewAccessor(0, GetNewestDataCapacity() + _headerSize);
//
//                 _accessor.Dispose(); // Dispose old accessor
//                 _mmf.Dispose(); // Dispose old MMF
//
//                 _mmf = newMmf;
//                 _accessor = newAccessor;
//             }
//             catch (FileNotFoundException e)
//             {
//                 //TODO: Handle error, By managed versions, this mmf should exist.
//                 throw;
//             }
//         }
//
//         private void Resize()
//         {
//             var sw = new Stopwatch();
//             sw.Start();
//
//             int currentDataSize = (int)(_accessor.Capacity - _headerSize);
//
//             // Default to double data size
//             var newDataSize = currentDataSize * 2;
//
//
//             var newVersion = UpdateToNewVersion(newDataSize);
//             var newMmf = MemoryMappedFile.CreateNew(
//                 $"{_mmfName}_{newVersion}",
//                 _headerSize + newDataSize,
//                 _mmfAccess
//             );
//             var newAccessor = newMmf.CreateViewAccessor(0, GetNewestDataCapacity() + _headerSize);
//
//             // Set new header
//             Header newHeader = new Header { Magic = 0xCAFEBABE, version = newVersion, IsCurrent = true };
//             newAccessor.Write(0, ref newHeader);
//
//             // Copy Data
//             byte[] data = new byte[currentDataSize];
//             _accessor.ReadArray(_headerSize, data, 0, currentDataSize);
//             newAccessor.WriteArray(_headerSize, data, 0, currentDataSize);
//
//             // Update old header to not current
//             _accessor.Read(0, out Header header);
//             header.IsCurrent = false;
//             _accessor.Write(0, ref header);
//
//             // Switch to new MMF and accessor
//             _accessor.Dispose(); // Dispose old accessor
//             _mmf.Dispose(); // Dispose old MMF
//             _mmf = newMmf;
//             _accessor = newAccessor;
//
//             _ewh.Set();
//             Console.WriteLine($"Resizing done in: {sw.ElapsedMilliseconds}ms");
//         }
//
//         protected void AddInternal(string key, string value)
//         {
//             ValidateVersionOfMmf();
//             if (!(_accessor.CanRead || _accessor.CanWrite))
//                 throw new InvalidOperationException(
//                     "Internal: MMF accessor is does not have the required Read/Write permissions.");
//             int currentPositionOffset = _headerSize;
//
//             while (true)
//             {
//                 // LOGIC IF WE HANDLE DUPLICATE INSERTS/ADDS OF KEYS
//                 // int keySize = _accessor.ReadInt32(currentPositionOffset + SizeOfKeyOffset);
//                 // byte[] keyBytes = new byte[keySize];
//                 // _accessor.ReadArray(currentPositionOffset + KeyOffset, keyBytes, 0, keySize);
//                 // string entryKey = System.Text.Encoding.Default.GetString(keyBytes);
//                 // if (key == entryKey)
//                 // {
//                 //     // TODO decide:
//                 //     //  Overwrite, Overwrite is logical, but cannot in current array structure.
//                 //     //  ignore,
//                 //     //  throw exception? Since we dont expect an add, if key exists and is retrieved.
//                 //     throw new ArgumentException("INTERNAL: An item with the same key has already been added.");
//                 // }
//
//                 _accessor.Read(currentPositionOffset, out EntryHeader eh);
//
//                 if (eh.Next is not 0) //not end of list
//                 {
//                     currentPositionOffset = eh.Next;
//                     continue;
//                 }
//
//                 int newEntryPosition = currentPositionOffset;
//                 if (eh.SizeOfKey != 0) // Check to determine if this is the first entry
//                 {
//                     newEntryPosition += KeyOffset + eh.SizeOfKey + eh.SizeOfKey;
//                 }
//
//                 byte[] newKeyBytes = Encoding.UTF8.GetBytes(key);
//                 byte[] newValueBytes = Encoding.UTF8.GetBytes(value);
//
//                 if (newEntryPosition + _entryHeaderSize + newKeyBytes.Length + newValueBytes.Length >
//                     _accessor.Capacity)
//                 {
//                     // Resize if Size of new entry exceeds current capacity of bytes
//                     Resize();
//                 }
//
//                 //Write new entry
//                 _accessor.Write(newEntryPosition + NextOffset, 0);
//                 _accessor.Write(newEntryPosition + SizeOfKeyOffset, newKeyBytes.Length);
//                 _accessor.Write(newEntryPosition + SizeOfValueOffset, newValueBytes.Length);
//                 _accessor.WriteArray(newEntryPosition + KeyOffset, newKeyBytes, 0, newKeyBytes.Length);
//                 _accessor.WriteArray(newEntryPosition + KeyOffset + newKeyBytes.Length, newValueBytes, 0,
//                     newValueBytes.Length);
//
//                 if (eh.SizeOfKey != 0)
//                 {
//                     // update previous entry's next value
//                     _accessor.Write(currentPositionOffset + NextOffset, newEntryPosition);
//                 }
//
//                 return;
//             }
//         }
//
//         // protected string GetListInternal()
//         // {
//         //     ValidateVersionOfMmf();
//         //     if (!(_accessor.CanRead))
//         //         throw new InvalidOperationException(
//         //             "Internal: MMF accessor is does not have the required Read/Write permissions.");
//         // }
//
//         protected bool TryGetValueInternal(string key, out string value)
//         {
//             ValidateVersionOfMmf();
//             if (!(_accessor.CanRead || _accessor.CanWrite))
//                 throw new InvalidOperationException(
//                     "Internal: MMF accessor is does not have the required Read/Write permissions.");
//
//             int currentPositionOffset = _headerSize;
//             while (true)
//             {
//                 _accessor.Read(currentPositionOffset, out EntryHeader eh);
//
//                 byte[] keyBytes = new byte[eh.SizeOfKey];
//                 _accessor.ReadArray(currentPositionOffset + KeyOffset, keyBytes, 0, eh.SizeOfKey);
//                 string entryKey = Encoding.Default.GetString(keyBytes);
//
//                 if (key == entryKey)
//                 {
//                     byte[] valueBytes = new byte[eh.SizeOfValue];
//                     _accessor.ReadArray(currentPositionOffset + KeyOffset + eh.SizeOfKey, valueBytes, 0,
//                         eh.SizeOfValue);
//                     value = Encoding.Default.GetString(valueBytes);
//                     return true;
//                 }
//
//                 if (eh.Next is 0) // End of list
//                 {
//                     break;
//                 }
//
//                 currentPositionOffset = eh.Next;
//             }
//
//             value = null;
//             return false;
//         }
//
//         public IEnumerator<string> GetEnumeratorInternal()
//         {
//             ValidateVersionOfMmf();
//
//             if (!(_accessor.CanRead || _accessor.CanWrite))
//                 throw new InvalidOperationException(
//                     "Internal: MMF accessor does not have the required Read/Write permissions.");
//
//             int currentPositionOffset = _headerSize;
//
//             while (true)
//             {
//                 _accessor.Read(currentPositionOffset, out EntryHeader eh);
//
//                 // Read key (but discard it, since we only want values)
//                 byte[] keyBytes = new byte[eh.SizeOfKey];
//                 _accessor.ReadArray(currentPositionOffset + KeyOffset, keyBytes, 0, eh.SizeOfKey);
//
//                 // Read value
//                 byte[] valueBytes = new byte[eh.SizeOfValue];
//                 _accessor.ReadArray(currentPositionOffset + KeyOffset + eh.SizeOfKey, valueBytes, 0, eh.SizeOfValue);
//                 string entryValue = System.Text.Encoding.Default.GetString(valueBytes);
//
//                 yield return entryValue;
//
//                 if (eh.Next <= 0) // End of list
//                     yield break;
//
//                 currentPositionOffset = eh.Next;
//             }
//
//         }
//
//         public class MmfLinkedListStringDictionary(
//             string name,
//             MemoryMappedFileAccess access = MemoryMappedFileAccess.ReadWrite)
//             : MmfLinkedListStringDictionaryBase(name, access), IEnumerable<string>
//         {
//             public void Add(string key, string value)
//             {
//                 // var sw = new Stopwatch();
//                 // sw.Start();
//                 using (new MutexLock(Mutex)) ;
//                 // Console.WriteLine($"Adding (Aquiring Mutex): {sw.ElapsedMilliseconds}ms");
//                 AddInternal(key, value);
//                 // Console.WriteLine($"Adding (Done): {sw.ElapsedMilliseconds}ms");
//             }
//
//             public bool TryGetValue(string key, out string value)
//             {
//                 using (new MutexLock(Mutex)) ;
//                 return TryGetValueInternal(key, out value);
//             }
//
//
//             public IEnumerator<string> GetEnumerator()
//             {
//                 return GetEnumeratorInternal();
//             }
//
//             IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
//         }
//     }

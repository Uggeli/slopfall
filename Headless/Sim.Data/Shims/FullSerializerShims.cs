// Compile-only stand-in for the FullSerializer library (ships as a Unity DLL
// in DFU). DFBlock.cs carries fs converters for the MOD world-data JSON
// format; that path is never exercised headlessly (WorldDataReplacement shim
// reports "no replacement" for everything), so these stubs only need to
// satisfy the compiler. fsData throws if anything ever does call into it.

using System;
using System.Collections.Generic;

namespace FullSerializer
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class fsObjectAttribute : Attribute
    {
        public Type Converter;
        public Type Processor;
    }

    public struct fsResult
    {
        public static fsResult Success => new fsResult();
        public bool Failed => false;
        public static fsResult operator +(fsResult a, fsResult b) => a;
    }

    public sealed class fsData
    {
        const string Msg = "FullSerializer shim: mod world-data serialization is not available headless.";
        public Dictionary<string, fsData> AsDictionary { get { throw new NotSupportedException(Msg); } }
        public List<fsData> AsList { get { throw new NotSupportedException(Msg); } }
        public string AsString { get { throw new NotSupportedException(Msg); } }
        public double AsDouble { get { throw new NotSupportedException(Msg); } }
        public bool IsNull { get { throw new NotSupportedException(Msg); } }
    }

    public abstract class fsDirectConverter<TModel>
    {
        protected abstract fsResult DoSerialize(TModel model, Dictionary<string, fsData> serialized);
        protected abstract fsResult DoDeserialize(Dictionary<string, fsData> data, ref TModel model);

        protected fsResult SerializeMember<T>(Dictionary<string, fsData> data, Type overrideConverterType, string name, T value)
            => fsResult.Success;

        protected fsResult DeserializeMember<T>(Dictionary<string, fsData> data, Type overrideConverterType, string name, out T value)
        {
            value = default;
            return fsResult.Success;
        }

        protected fsResult CheckKey(Dictionary<string, fsData> data, string key, out fsData subitem)
        {
            subitem = null;
            return fsResult.Success;
        }
    }

    public abstract class fsObjectProcessor
    {
        public virtual void OnAfterSerialize(Type storageType, object instance, ref fsData data) { }
    }
}

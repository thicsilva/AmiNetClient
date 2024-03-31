using System.Reflection;
using System.Text;

namespace AnAmiClient;

public partial class AmiNetMessage
{
    internal static readonly byte[] TerminatorBytes = {13, 10};
    internal static readonly char[] TerminatorChars = {'\r', '\n'};
    private static readonly char[] Separator = {':'};

    public static AmiNetMessage FromBytes(byte[] bytes)
    {
        AmiNetMessage result = new();
        for (int lineNr = 1;; lineNr++)
        {
            int crlfPos = bytes.Find(AmiNetMessage.TerminatorBytes, 0, bytes.Length);
            if (crlfPos == -1)
                throw new ArgumentException($"Unexpected end of message after {lineNr} line(s)", nameof(bytes));

            string line = Encoding.UTF8.GetString(bytes.Slice(0, crlfPos));
            bytes = bytes.Slice(crlfPos + AmiNetMessage.TerminatorBytes.Length);

            if (string.IsNullOrEmpty(line))
                break;

            string[] kvp = line.Split(Separator, 2);
            if (kvp.Length != 2)
                throw new ArgumentException($"Malformed field on line {lineNr}", nameof(bytes));
            
            result.Add(kvp[0], kvp[1]);
        }
        
        return result;
    }

    public static AmiNetMessage FromString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return FromBytes(bytes);
    }

    public byte[] ToBytes()
    {
        MemoryStream stream = new();

        using(StreamWriter writer = new(stream, new UTF8Encoding(false)))
        {
            foreach(KeyValuePair<string, string> field in Fields)
            {
                writer.Write(field.Key);
                writer.Write(": ");
                writer.Write(field.Value);
                writer.Write(TerminatorChars);
            }

            writer.Write(TerminatorChars);
        }

        return stream.ToArray();
    }

    public T ToObject<T>() where T : class, new()
    {
        T someObject = new T();
        Type someObjectType = someObject.GetType();

        foreach (KeyValuePair<string, string> item in Fields)
        {
            string key = char.ToUpper(item.Key[0]) + item.Key[1..];
            PropertyInfo targetProperty = someObjectType.GetProperty(key);
            if (targetProperty==null)
                continue;
            
            if (targetProperty.PropertyType == item.Value.GetType())
            {
                targetProperty.SetValue(someObject, item.Value);
            }
            else
            {
                try
                {
                    object value = Convert.ChangeType(item.Value, targetProperty.PropertyType);
                    targetProperty.SetValue(someObject, value);
                }
                catch
                {
                    // ignored
                }
            }
        }

        return someObject;
    }

    public override string ToString()
    {
        return Encoding.UTF8.GetString(ToBytes());
    }
}
using System.Collections;
using System.Globalization;
using System.Text;

namespace AnAmiClient;

public partial class AmiNetMessage : IEnumerable<KeyValuePair<string, string>>
{
    public readonly List<KeyValuePair<string, string>> Fields = new();
    public readonly List<AmiNetMessage> Responses = new();
    public DateTimeOffset Timestamp { get; private set; }

    public bool IsSuccess
    {
        get
        {
            return Fields.Any(kvp => kvp.Key.Equals("Response", StringComparison.OrdinalIgnoreCase) && kvp.Value.Equals("Success", StringComparison.OrdinalIgnoreCase));
        }
    }

    public AmiNetMessage()
    {
        Timestamp = DateTimeOffset.UtcNow;
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key not be null", nameof(key));
            key = key.Trim();

            return Fields.Where(kvp => kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                .Select(el => el.Value)
                .FirstOrDefault();
        }

        private set
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key not be null", nameof(key));
            key = key.Trim();
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            value = value.Trim();
            if (key.Equals("ActionID", StringComparison.OrdinalIgnoreCase))
                Fields.RemoveAll(field => field.Key.Equals("ActionID", StringComparison.OrdinalIgnoreCase));
            Fields.Add(new KeyValuePair<string, string>(key, value));
            if (key.Equals("Action", StringComparison.OrdinalIgnoreCase) && !Fields.Any(field =>
                    field.Key.Equals("ActionID", StringComparison.OrdinalIgnoreCase)))
                Fields.Add(new KeyValuePair<string, string>("ActionID", Guid.NewGuid().ToString("D")));
            if (!key.Equals("Timestamp", StringComparison.OrdinalIgnoreCase))
                return;
            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
                return;
            DateTime dt = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Timestamp = dt.AddSeconds(seconds);
        }
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        return Fields.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(string key, string value)
    {
        if (Fields.Any(kvp => kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            this[key] += string.Concat(Encoding.UTF8.GetString(TerminatorBytes), value);
        else
            this[key] = value;
    }
}
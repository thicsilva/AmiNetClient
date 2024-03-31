using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace AnAmiClient;

public sealed partial class AmiNetClient : IDisposable
{
    public sealed class DataEventArgs : EventArgs
    {
        public readonly byte[] Data;

        internal DataEventArgs(byte[] data) => Data = data;
    }

    public event EventHandler<DataEventArgs> DataSent;
    public event EventHandler<DataEventArgs> DataReceived;
    private readonly TcpClient _tcpClient = new();
    public bool IsConnected =>  _stream != null;
    private NetworkStream _stream;
    private StreamReader _reader;
    private BinaryWriter _writer;
    private readonly CancellationTokenSource _tokenSource = new();
    private readonly CancellationToken _cancellationToken;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<AmiNetMessage>> _inFlight =
        new(Environment.ProcessorCount, 16384, StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<string> _linesQueue = new();

    private readonly
        ConcurrentDictionary<string, Tuple<AmiNetMessage,
            Action<AmiNetMessage, AmiNetMessage, TaskCompletionSource<AmiNetMessage>>,
            TaskCompletionSource<AmiNetMessage>>> _responseInFlight =
            new(Environment.ProcessorCount, 32768, StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<IEnumerable<KeyValuePair<string, string>>, Func<AmiNetMessage, Task>>
        _eventSubscriptions =
            new(Environment.ProcessorCount, 65536);

    public AmiNetClient(string ip, int port) : this(new TcpClient(ip, port))
    {
    }

    internal AmiNetClient(TcpClient tcpClient)
    {
        _stream = tcpClient.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8);
        _writer = new BinaryWriter(_stream, Encoding.UTF8);
        _cancellationToken = _tokenSource.Token;
    }

    public void StartAsync()
    {
        Task.Factory.StartNew(LineReaderProcessor, TaskCreationOptions.LongRunning);
        Task.Factory.StartNew(PipelineProcessor, TaskCreationOptions.LongRunning);
    }

    private void LineReaderProcessor()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            string line = ReadLine();
            if (line != null)
                _linesQueue.Enqueue(line);
        }
    }

    private string ReadLine()
    {
        string line;
        try
        {
            line = _reader.ReadLine();
        }
        catch
        {
            line = null;
        }

        return line;
    }

    private async Task PipelineProcessor()
    {
        try
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                string message = GetMessageFromQueue();
                if (string.IsNullOrEmpty(message))
                    continue;

                AmiNetMessage parsedMessage = AmiNetMessage.FromString(message);
                DataReceived?.Invoke(this, new DataEventArgs(parsedMessage.ToBytes()));
                if (parsedMessage.Fields.FirstOrDefault().Key.Equals("Response", StringComparison.OrdinalIgnoreCase) &&
                    _inFlight.TryGetValue(parsedMessage["ActionID"], out TaskCompletionSource<AmiNetMessage> tcs))
                    ResponseProcessor(tcs, parsedMessage);
                else if (parsedMessage.Fields.Any(kvp =>
                             kvp.Key.Contains("ActionID", StringComparison.OrdinalIgnoreCase)) &&
                         _responseInFlight.TryGetValue(
                             parsedMessage["ActionID"],
                             out Tuple<AmiNetMessage,
                                 Action<AmiNetMessage, AmiNetMessage, TaskCompletionSource<AmiNetMessage>>,
                                 TaskCompletionSource<AmiNetMessage>> exp))
                    exp.Item2.Invoke(exp.Item1, parsedMessage, exp.Item3);
                
                await EventSubscriptionProcessor(parsedMessage);
            }
        }
        catch (Exception ex)
        {
            Stop(ex);
        }

        Stop();
    }

    private string GetMessageFromQueue()
    {
        StringBuilder messageBuilder = new();
        while (!_cancellationToken.IsCancellationRequested)
        {
            if (!_linesQueue.TryDequeue(out string line))
                continue;

            if (string.IsNullOrEmpty(line))
            {
                messageBuilder.Append("\r\n");
                break;
            }

            if (line.Contains("Asterisk Call Manager"))
                continue;

            messageBuilder.AppendLine(line);
        }

        return messageBuilder.ToString();
    }

    private async Task EventSubscriptionProcessor(AmiNetMessage parsedMessage)
    {
        KeyValuePair<IEnumerable<KeyValuePair<string, string>>, Func<AmiNetMessage, Task>> evt =
            _eventSubscriptions.FirstOrDefault(e => !e.Key.Except(parsedMessage.Fields).Any());
        if (evt.Value != null)
            await evt.Value(parsedMessage);
    }

    private void ResponseProcessor(TaskCompletionSource<AmiNetMessage> tcs, AmiNetMessage parsedMessage)
    {
        if (parsedMessage.Fields.FirstOrDefault().Key.Equals("Response"))
            _inFlight.TryRemove(parsedMessage["ActionID"], out _);
        _responseInFlight.TryAdd(parsedMessage["ActionID"],
            new Tuple<AmiNetMessage, Action<AmiNetMessage, AmiNetMessage, TaskCompletionSource<AmiNetMessage>>,
                TaskCompletionSource<AmiNetMessage>>(parsedMessage, AddResponseToMessage, tcs));
        AddResponseToMessage(parsedMessage, parsedMessage, tcs);
    }

    private void AddResponseToMessage(AmiNetMessage parsedMessage, AmiNetMessage response,
        TaskCompletionSource<AmiNetMessage> tcs)
    {
        if (!response.Any(kvp => kvp.Key.Equals("EventList") || kvp.Key.Equals("Event")))
        {
            _responseInFlight.TryRemove(parsedMessage["ActionID"], out _);
            tcs.SetResult(parsedMessage);
            return;
        }

        if (!response.FirstOrDefault().Key.Equals("Event", StringComparison.OrdinalIgnoreCase) || !response
                .FirstOrDefault()
                .Value.Contains("Complete", StringComparison.OrdinalIgnoreCase))
        {
            if (parsedMessage.Equals(response))
                return;

            lock (parsedMessage.Responses)
                parsedMessage.Responses.Add(response);
            return;
        }

        _responseInFlight.TryRemove(parsedMessage["ActionID"], out _);
        tcs.SetResult(parsedMessage);
    }

    public async Task<AmiNetMessage> Publish(AmiNetMessage action)
    {
        if (_stream == null)
            throw new InvalidOperationException("Client not started");

        TaskCompletionSource<AmiNetMessage> tcs = new(TaskCreationOptions.AttachedToParent);
        if (!_inFlight.TryAdd(action["ActionID"], tcs))
            throw new Exception("A message with the same ActionID is already in flight");

        AmiNetMessage task;
        try
        {
            byte[] buffer = action.ToBytes();
            lock (_stream)
            {
                _writer.Write(buffer, 0, buffer.Length);
                _writer.Flush();
            }

            DataSent?.Invoke(this, new DataEventArgs(buffer));

            task = await tcs.Task;
        }
        catch (Exception ex)
        {
            Stop(ex);
            throw;
        }
        finally
        {
            _inFlight.TryRemove(action["ActionID"], out _);
        }

        return task;
    }

    public void AddEventListener(AmiNetEvent evt,
        Func<AmiNetMessage, Task> func)
    {
        _eventSubscriptions.TryAdd(evt.Fields, func);
    }


    public void Dispose()
    {
        Stop();
    }

    public void Stop() => Stop(null);

    private void Stop(Exception ex)
    {
        if (_stream == null)
            return;

        try
        {
            foreach (TaskCompletionSource<AmiNetMessage> flightValue in _inFlight.Values)
            {
                if (ex != null)
                    flightValue.TrySetException(ex);
                else
                    flightValue.TrySetCanceled(_cancellationToken);
            }

            _eventSubscriptions.Clear();
            _responseInFlight.Clear();
            _linesQueue.Clear();

            lock (_stream)
            {
                _stream.Dispose();
                _stream = null;
                _tokenSource.Cancel();
            }

            _reader.Dispose();
            _reader = null;

            _writer.Dispose();
            _writer = null;

            _tcpClient.Close();
        }
        catch
        {
            // ignored
        }
    }
}
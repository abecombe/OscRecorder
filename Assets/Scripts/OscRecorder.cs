using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using extOSC;
using RosettaUI;
using SFB;
using UnityEngine;

[Serializable]
public class OscRecorderParams : IParams
{
    public string LoadFolderPath = "";

    public int OscReceivePort = 10000;
    public string OscReceivePath = "";

    public string OscTransmitIp = "127.0.0.1";
    public int OscTransmitPort = 10000;
    public string OscTransmitPath = "";

    public float RecordingMaximumTime = 60f;
    public float RecordingMaximumDataSize = 1000f;
}

public class OscRecorder : MonoBehaviour
{
    #region Properties
    [SerializeField] private OscRecorderParams _params;

    [SerializeField] private RosettaUIRoot _rosettaUIRoot;

    [SerializeField] private OSCReceiver _oscReceiver;
    [SerializeField] private OSCTransmitter _oscTransmitter;

    private static string DefaultRecordingDataFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "OscData");

    private const string RecordingDataFilePath = "data.bin";
    private const string RecordingInfoFilePath = "info.txt";
    private const string RecordingTypeFilePath = "type.txt";
    private const string RecordingStringFilePath = "string.txt";
    private const string RecordingTimeFilePath = "time.bin";

    private const float TimeWriteInterval = 1f;

    // Recording Variables
    private DateTime _recordStartTime;

    private string _saveFolderPath;
    private FileStream _saveDataStream;
    private StreamWriter _saveInfoStream;
    private StreamWriter _saveTypeStream;
    private StreamWriter _saveStringStream;
    private FileStream _saveTimeStream;

    private bool _isRecording = false;
    private bool _isFirstOscReceived = false;
    private int _recordingCount = 0;
    private List<OSCValueType> _receiveOscTypes = new();
    private List<string> _receiveOscStrings = new();
    private byte[] _bytesToWrite;
    private int _numBytesToWrite = 0;
    private int _timeWriteCount = 0;

    // Playing Variables
    private FileStream _loadDataStream;
    private FileStream _loadTimeStream;

    private OSCMessage _oscTransmitMessage;
    private bool _isPlaying = false;
    private bool _isPausing = true;
    private bool _isLooping = true;
    private float _playSpeed = 1f;
    private double _currentTime = 0d;
    private double _totalTime = 0f;
    private List<OSCValueType> _transmitOscTypes = new();
    private List<string> _transmitOscStrings = new();
    private byte[] _bytesToRead;
    private byte[] _bytesToTimeRead = new byte[4];
    private int _numBytesToRead = 0;
    private string _playingComment = "Waiting for Load Folder.";
    #endregion

    #region Helper Functions
    private static string TimeSpanToHHmmss(TimeSpan span)
    {
        return span.Hours.ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00");
    }

    private static string TimeSpanToHHmmssfff(TimeSpan span)
    {
        return span.Hours.ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00") + "." + span.Milliseconds.ToString("000");
    }
    #endregion

    #region Recording Functions
    private void StartRecording()
    {
        _recordStartTime = DateTime.Now;

        _recordingCount = 0;
        _isRecording = true;
        _isFirstOscReceived = true;
        _receiveOscTypes.Clear();
        _receiveOscStrings.Clear();

        OpenWriteFiles();
        _oscReceiver.enabled = true;
        SetOscReceiver();
    }

    private void StopRecording(string comment = "")
    {
        if (!_isRecording) return;
        _isRecording = false;

        _saveInfoStream.WriteLine(comment);
        _saveInfoStream.WriteLine("Record Start Time: " + _recordStartTime.ToString("yyyy/MM/dd HH:mm:ss:ff"));
        _saveInfoStream.WriteLine("Record Stop Time: " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss:ff"));
        _saveInfoStream.WriteLine("Total Time: " + TimeSpanToHHmmssfff(DateTime.Now - _recordStartTime));

        ShowRecordingFinishedPopup(comment);

        CloseWriteFiles();
        _oscReceiver.enabled = false;
    }

    private void OpenWriteFiles()
    {
        _saveFolderPath = Path.Combine(DefaultRecordingDataFolderPath, _recordStartTime.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(_saveFolderPath);

        string saveDataPath = Path.Combine(_saveFolderPath, RecordingDataFilePath);
        _saveDataStream = new FileStream(saveDataPath, FileMode.CreateNew, FileAccess.Write);

        string saveInfoPath = Path.Combine(_saveFolderPath, RecordingInfoFilePath);
        _saveInfoStream = new StreamWriter(saveInfoPath, false);

        string saveTypePath = Path.Combine(_saveFolderPath, RecordingTypeFilePath);
        _saveTypeStream = new StreamWriter(saveTypePath, false);

        string saveStringPath = Path.Combine(_saveFolderPath, RecordingStringFilePath);
        _saveStringStream = new StreamWriter(saveStringPath, false);

        string saveTimePath = Path.Combine(_saveFolderPath, RecordingTimeFilePath);
        _saveTimeStream = new FileStream(saveTimePath, FileMode.CreateNew, FileAccess.Write);
        _saveTimeStream.Write(BitConverter.GetBytes(0), 0, 4);
        _timeWriteCount = 1;
    }

    private void CloseWriteFiles()
    {
        _saveDataStream?.Flush();
        _saveDataStream?.Close();
        _saveDataStream = null;

        _saveInfoStream?.Flush();
        _saveInfoStream?.Close();
        _saveInfoStream = null;

        _saveTypeStream?.Flush();
        _saveTypeStream?.Close();
        _saveTypeStream = null;

        _saveStringStream?.Flush();
        _saveStringStream?.Close();
        _saveStringStream = null;

        _saveTimeStream?.Flush();
        _saveTimeStream?.Close();
        _saveTimeStream = null;
    }

    private void SetOscReceiver()
    {
        _oscReceiver.LocalPort = _params.OscReceivePort;
        _oscReceiver.ClearBinds();
        _oscReceiver.Bind(_params.OscReceivePath, OnOscReceived);
    }

    private bool ValidateMessageFormat(OSCMessage message)
    {
        if (message.Values.Count != _receiveOscTypes.Count)
            return false;

        return !message.Values.Where((oscValue, i) => oscValue.Type != _receiveOscTypes[i]).Any();
    }

    private bool OnFirstOscReceived(OSCMessage message)
    {
        foreach (var value in message.Values)
        {
            _receiveOscTypes.Add(value.Type);
            _saveTypeStream.WriteLine(value.Type.ToString());

            if (value.Type is OSCValueType.Int or OSCValueType.Float or OSCValueType.String) continue;
            StopRecording("Unsupported OSC Value Type: " + value.Type);
            return false;
        }

        _numBytesToWrite = _receiveOscTypes.Count * 4 + 8;
        _bytesToWrite = new byte[_numBytesToWrite];

        return true;
    }

    private void OnOscReceived(OSCMessage message)
    {
        if (!_isRecording)
            return;

        if (_isFirstOscReceived)
        {
            _isFirstOscReceived = false;
            if (!OnFirstOscReceived(message)) return;
        }

        if (!ValidateMessageFormat(message))
            return;

        int byteOffset = 0;

        long elapsedTicks = (DateTime.Now - _recordStartTime).Ticks;
        Array.Copy(BitConverter.GetBytes(elapsedTicks), 0, _bytesToWrite, byteOffset, 8);
        byteOffset += 8;

        foreach (var value in message.Values)
        {
            switch (value.Type)
            {
                case OSCValueType.Int:
                    Array.Copy(BitConverter.GetBytes(value.IntValue), 0, _bytesToWrite, byteOffset, 4);
                    break;
                case OSCValueType.Float:
                    Array.Copy(BitConverter.GetBytes(value.FloatValue), 0, _bytesToWrite, byteOffset, 4);
                    break;
                case OSCValueType.String:
                    if (!_receiveOscStrings.Contains(value.StringValue))
                    {
                        _receiveOscStrings.Add(value.StringValue);
                        _saveStringStream.WriteLine(value.StringValue);
                    }
                    Array.Copy(BitConverter.GetBytes(_receiveOscStrings.IndexOf(value.StringValue)), 0, _bytesToWrite, byteOffset, 4);
                    break;
            }
            byteOffset += 4;
        }

        _saveDataStream.Write(_bytesToWrite, 0, _numBytesToWrite);

        _recordingCount++;
    }

    private void WriteTimeInfo()
    {
        if (!_isRecording)
            return;

        if ((DateTime.Now - _recordStartTime).TotalSeconds >= _timeWriteCount * (double)TimeWriteInterval)
        {
            _timeWriteCount++;
            _saveTimeStream.Write(BitConverter.GetBytes(_recordingCount), 0, 4);
        }
    }

    private void ShowRecordingFinishedPopup(string comment = "")
    {
        string startTime = _recordStartTime.ToString("yyyy/MM/dd HH:mm:ss:ff");
        string stopTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss:ff");
        string totalTime = TimeSpanToHHmmssfff(DateTime.Now - _recordStartTime);
        var dataFormat = _receiveOscTypes.Select(type => type.ToString()).ToList();
        var dataStrings = _receiveOscStrings.ToList();
        string dataSize = ((float)_saveDataStream.Length / 1024 / 1024).ToString("0.000");
        string savedFolderPath = _saveFolderPath;

        var okButton = UI.Button("OK", () => { }).SetButtonColor(Color.blue * 0.3f);
        var window = UI.Window("Recording Status",
            UI.Column(
                CustomUI.HorizontalLine(0f, 0f),
                UI.Indent(UI.Column(
                    UI.Label(comment),
                    UI.Box(UI.Indent(UI.Column(
                        UI.FieldReadOnly("Start Time", () => startTime),
                        UI.FieldReadOnly("Stop Time", () => stopTime),
                        UI.FieldReadOnly("Total Time", () => totalTime),
                        UI.ListReadOnly("Data Format", () => dataFormat).Close(),
                        UI.ListReadOnly("Data Strings", () => dataStrings).Close(),
                        UI.FieldReadOnly("Data Size (MB)", () => dataSize),
                        UI.Row(
                            UI.Label("Saved Folder Path"),
                            UI.Space(),
                            UI.Button("copy to clipboard", () => Clipboard.SetText(savedFolderPath))
                        ),
                        UI.HelpBox(UI.Label(() => savedFolderPath)),
                        CustomUI.BlankLine(5f),
                        okButton,
                        CustomUI.BlankLine(3f)
                    )))
                ))
            )
        );
        okButton.onClick += () => window.Close();

        _rosettaUIRoot.Build(window);
    }
    #endregion

    #region Playing Functions
    private void OpenReadFolder()
    {
        if (!OpenReadFiles())
        {
            _isPlaying = false;
            _isPausing = true;
            _currentTime = 0d;
            _totalTime = 0d;
            return;
        }

        _isPlaying = true;
        _isPausing = true;
        _currentTime = 0d;

        SetOscTransmitter();
    }

    private bool OpenReadFiles()
    {
        CloseReadFiles();

        if (!Directory.Exists(_params.LoadFolderPath))
        {
            _playingComment = "Load Folder is not exist.";
            return false;
        }

        string loadDataPath = Path.Combine(_params.LoadFolderPath, RecordingDataFilePath);
        string loadTypePath = Path.Combine(_params.LoadFolderPath, RecordingTypeFilePath);
        string loadStringPath = Path.Combine(_params.LoadFolderPath, RecordingStringFilePath);
        string loadTimePath = Path.Combine(_params.LoadFolderPath, RecordingTimeFilePath);
        if (!File.Exists(loadDataPath))
        {
            _playingComment = "Data File is not exist.";
            return false;
        }
        if (!File.Exists(loadTypePath))
        {
            _playingComment = "Type File is not exist.";
            return false;
        }
        if (!File.Exists(loadStringPath))
        {
            _playingComment = "String File is not exist.";
            return false;
        }
        if (!File.Exists(loadTimePath))
        {
            _playingComment = "Time File is not exist.";
            return false;
        }

        _loadDataStream = new FileStream(loadDataPath, FileMode.Open, FileAccess.Read);
        if (_loadDataStream.Length == 0)
        {
            _playingComment = "Data File is empty.";
            return false;
        }

        _transmitOscTypes.Clear();
        using (StreamReader reader = new StreamReader(loadTypePath))
        {
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (!Enum.TryParse(line, out OSCValueType type))
                {
                    _playingComment = "Unsupported OSC Value Type: " + line;
                    return false;
                }
                if (type is not (OSCValueType.Int or OSCValueType.Float or OSCValueType.String))
                {
                    _playingComment = "Unsupported OSC Value Type: " + type;
                    return false;
                }
                _transmitOscTypes.Add(type);
            }
        }
        if (_transmitOscTypes.Count == 0)
        {
            _playingComment = "Type File data is not enough.";
            return false;
        }

        _transmitOscStrings.Clear();
        using (StreamReader reader = new StreamReader(loadStringPath))
        {
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                _transmitOscStrings.Add(line);
            }
        }
        if (_transmitOscTypes.Contains(OSCValueType.String) && _transmitOscStrings.Count == 0)
        {
            _playingComment = "String File data is not enough.";
            return false;
        }

        _loadTimeStream = new FileStream(loadTimePath, FileMode.Open, FileAccess.Read);

        _numBytesToRead = _transmitOscTypes.Count * 4 + 8;
        _bytesToRead = new byte[_numBytesToRead];

        _loadDataStream.Seek(-_numBytesToRead, SeekOrigin.End);
        _loadDataStream.Read(_bytesToRead, 0, 8);
        _totalTime = TimeSpan.FromTicks(BitConverter.ToInt64(_bytesToRead, 0)).TotalSeconds;
        _loadDataStream.Seek(0, SeekOrigin.Begin);

        if ((int)(_loadTimeStream.Length / 4) < (int)(_totalTime / TimeWriteInterval))
        {
            _playingComment = "Time File data is not enough.";
            return false;
        }

        _playingComment = "Files Loaded Successfully.";
        return true;
    }

    private void CloseReadFiles()
    {
        _loadDataStream?.Flush();
        _loadDataStream?.Close();
        _loadDataStream = null;
        _loadTimeStream?.Flush();
        _loadTimeStream?.Close();
        _loadTimeStream = null;
    }

    private void TransmitOsc()
    {
        if (!_isPlaying || _isPausing)
            return;

        while (true)
        {
            _loadDataStream.Read(_bytesToRead, 0, _numBytesToRead);

            long elapsedTicks = BitConverter.ToInt64(_bytesToRead, 0);
            if (TimeSpan.FromTicks(elapsedTicks).TotalSeconds > _currentTime)
            {
                _loadDataStream.Seek(-_numBytesToRead, SeekOrigin.Current);
                break;
            }

            for (int i = 0; i < _transmitOscTypes.Count; i++)
            {
                var type = _transmitOscTypes[i];
                switch (type)
                {
                    case OSCValueType.Int:
                        _oscTransmitMessage.Values[i] = OSCValue.Int(BitConverter.ToInt32(_bytesToRead, 8 + i * 4));
                        break;
                    case OSCValueType.Float:
                        _oscTransmitMessage.Values[i] = OSCValue.Float(BitConverter.ToSingle(_bytesToRead, 8 + i * 4));
                        break;
                    case OSCValueType.String:
                        _oscTransmitMessage.Values[i] = OSCValue.String(_transmitOscStrings[Math.Clamp(BitConverter.ToInt32(_bytesToRead, 8 + i * 4), 0, _transmitOscStrings.Count - 1)]);
                        break;
                }
            }

            _oscTransmitter.Send(_oscTransmitMessage);

            if (_loadDataStream.Position != _loadDataStream.Length) continue;

            _loadDataStream.Seek(0, SeekOrigin.Begin);
            _currentTime = 0d;
            if (!_isLooping)
            {
                _isPausing = true;
            }
            return;
        }
    }

    private void SetDataStreamPosition(int count)
    {
        int seekPosition = Math.Min(4 * count, (int)_loadTimeStream.Length - 4);
        _loadTimeStream.Seek(seekPosition, SeekOrigin.Begin);
        _loadTimeStream.Read(_bytesToTimeRead, 0, 4);

        _loadDataStream.Seek(_numBytesToRead * BitConverter.ToInt32(_bytesToTimeRead, 0), SeekOrigin.Begin);
    }

    private void SetOscTransmitter()
    {
        _oscTransmitter.enabled = true;
        _oscTransmitter.RemoteHost = _params.OscTransmitIp;
        _oscTransmitter.RemotePort = _params.OscTransmitPort;
        _oscTransmitMessage = new OSCMessage(_params.OscTransmitPath);
        foreach (var type in _transmitOscTypes)
        {
            switch (type)
            {
                case OSCValueType.Int:
                    _oscTransmitMessage.AddValue(OSCValue.Int(0));
                    break;
                case OSCValueType.Float:
                    _oscTransmitMessage.AddValue(OSCValue.Float(0f));
                    break;
                case OSCValueType.String:
                    _oscTransmitMessage.AddValue(OSCValue.String(""));
                    break;
            }
        }
    }
    #endregion

    #region RosettaUI Functions
    private Element CreateElement()
    {
        var window =
            UI.Window(
                UI.Indent(UI.Column(
                    CustomUI.BlankLine(5f),
                    CustomUI.BoldLabel("OSC Recorder / Player"),
                    CustomUI.HorizontalLine(0f, 0f),
                    UI.Box(UI.Indent(UI.Column(
                        CustomUI.BlankLine(10f),
                        UI.Row(
                            UI.Button("Load Default", () =>
                            {
                                FileSaveLoader.LoadDefaultParams(_params);
                                OpenReadFolder();
                                SetOscReceiver();
                            }).SetWidth(330f),
                            UI.Button("Save Default", () => FileSaveLoader.SaveDefaultParams(_params)).SetWidth(330f)
                        ),
                        CustomUI.BlankLine(5f),
                        UI.Row(
                            UI.Button("Load File", () =>
                            {
                                FileSaveLoader.LoadParamsWithFileBrowser(_params);
                                OpenReadFolder();
                                SetOscReceiver();
                            }).SetWidth(330f),
                            UI.Button("Save File", () => FileSaveLoader.SaveParamsWithFileBrowser(_params)).SetWidth(330f)
                        ),
                        CustomUI.BlankLine(13f),
                        UI.Tabs(
                            ("Record", UI.Column(
                                CustomUI.BlankLine(5f),
                                CustomUI.BoldLabel("OSC Settings"),
                                UI.Indent(UI.Column(
                                    UI.Field("Receive Port", () => _params.OscReceivePort).RegisterValueChangeCallback(SetOscReceiver),
                                    UI.Field("Receive Path", () => _params.OscReceivePath).RegisterValueChangeCallback(SetOscReceiver)
                                )),
                                CustomUI.BlankLine(5f),
                                CustomUI.BoldLabel("Recording Settings"),
                                UI.Indent(UI.Column(
                                    UI.Field("Maximum Time (m)", () => _params.RecordingMaximumTime),
                                    UI.Field("Maximum Data Size (MB)", () => _params.RecordingMaximumDataSize)
                                )),
                                CustomUI.BlankLine(5f),
                                UI.DynamicElementOnStatusChanged(() =>
                                    _isRecording, isRecording => isRecording
                                    ? UI.Column(
                                        CustomUI.BoldLabel("Recording Information"),
                                        UI.Indent(UI.Column(
                                            UI.FieldReadOnly("Start Time", () => _recordStartTime.ToString("yyyy/MM/dd HH:mm:ss:ff")),
                                            UI.FieldReadOnly("Elapsed Time", () => TimeSpanToHHmmssfff(DateTime.Now - _recordStartTime)),
                                            UI.ListReadOnly("Data Format", () => _receiveOscTypes).Close(),
                                            UI.ListReadOnly("Data Strings", () => _receiveOscStrings).Close(),
                                            UI.FieldReadOnly("Data Size (MB)", () => ((float)_saveDataStream.Length / 1024 / 1024).ToString("0.000")),
                                            UI.Label("Saved Folder Path"),
                                            UI.HelpBox(UI.Label(() => _saveFolderPath))
                                        )),
                                        CustomUI.BlankLine(5f),
                                        UI.Button("Stop Recording", () => StopRecording("Recording Successfully Finished")).SetButtonColor(Color.blue * 0.3f)
                                    )
                                    : UI.Button("Start Recording", StartRecording).SetButtonColor(Color.red * 0.3f)
                                ),
                                CustomUI.BlankLine(3f)
                            )),
                            ("Playback", UI.Column(
                                CustomUI.BlankLine(5f),
                                CustomUI.BoldLabel("OSC Settings"),
                                UI.Indent(UI.Column(
                                    UI.Field("Transmit IP", () => _params.OscTransmitIp).RegisterValueChangeCallback(SetOscTransmitter),
                                    UI.Field("Transmit Port", () => _params.OscTransmitPort).RegisterValueChangeCallback(SetOscTransmitter),
                                    UI.Field("Transmit Path", () => _params.OscTransmitPath).RegisterValueChangeCallback(SetOscTransmitter)
                                )),
                                CustomUI.BlankLine(5f),
                                CustomUI.BoldLabel("Playback Controller"),
                                CustomUI.BlankLine(3f),
                                UI.Row(
                                    UI.HelpBox(UI.Label(() => _params.LoadFolderPath)),
                                    UI.Button("Open Panel", () =>
                                    {
                                        var paths = StandaloneFileBrowser.OpenFolderPanel("Load Folder", DefaultRecordingDataFolderPath, false);
                                        if (paths.Length == 0) return;
                                        _params.LoadFolderPath = paths[0];
                                        OpenReadFolder();
                                    }).SetButtonColor(Color.yellow * 0.3f).SetWidth(140f)
                                ),
                                CustomUI.BlankLine(10f),
                                UI.HelpBox(UI.Label(() => _playingComment)),
                                UI.DynamicElementOnStatusChanged(() =>
                                    _isPlaying, isPlaying => isPlaying
                                    ? UI.Column(
                                        CustomUI.BlankLine(10f),
                                        UI.DynamicElementOnStatusChanged(() =>
                                            _totalTime, totalTime =>
                                            UI.Column(
                                                UI.Row(
                                                    CustomUI.SliderWithoutInputField(null, () => (float)_currentTime, value =>
                                                    {
                                                        _currentTime = Math.Clamp(value, 0d, totalTime);
                                                        _currentTime -= _currentTime % TimeWriteInterval;
                                                        SetDataStreamPosition((int)Math.Round(_currentTime / TimeWriteInterval));
                                                    }, 0f, (float)totalTime),
                                                    UI.Space().SetWidth(5f)
                                                ),
                                                UI.Row(
                                                    UI.DynamicElementOnStatusChanged(() =>
                                                            _isPausing, isPausing => isPausing
                                                            ? UI.Button("play", () => _isPausing = false).SetWidth(70f).SetButtonColor(Color.red * 0.3f)
                                                            : UI.Button("pause", () => _isPausing = true).SetWidth(70f).SetButtonColor(Color.blue * 0.3f)
                                                    ),
                                                    UI.Space(),
                                                    UI.Label(() => " " + TimeSpanToHHmmssfff(TimeSpan.FromSeconds(_currentTime))).SetWidth(128f),
                                                    UI.Label(() => "/ " + TimeSpanToHHmmssfff(TimeSpan.FromSeconds(totalTime))).SetWidth(137f)
                                                )
                                            )
                                        ),
                                        UI.Row(
                                            UI.Space(),
                                            UI.Label("speed "),
                                            UI.Field(null, () => _playSpeed, value => _playSpeed = Mathf.Clamp(value, 0.01f, 30f)).SetWidth(70f),
                                            UI.Label(" loop "),
                                            UI.Field(null, () => _isLooping),
                                            UI.Space().SetWidth(5f)
                                        )
                                    )
                                    : null
                                ),
                                CustomUI.BlankLine(3f)
                            ))
                        )
                    )))
                ))
            );
        window.SetWidth(750f);
        window.Closable = false;

        return window;
    }

    private void InitRosettaUI()
    {
        _rosettaUIRoot.Build(CreateElement());
    }
    #endregion

    #region MonoBehaviour
    private void Start()
    {
        InitRosettaUI();
        FileSaveLoader.LoadDefaultParams(_params);
        OpenReadFolder();
        SetOscReceiver();
    }

    private void Update()
    {
        if (_isRecording)
        {
            WriteTimeInfo();

            if (DateTime.Now - _recordStartTime > TimeSpan.FromMinutes(_params.RecordingMaximumTime) ||
                (float)_saveDataStream.Length / 1024 / 1024 > _params.RecordingMaximumDataSize)
                StopRecording("Recording Successfully Finished");
        }

        if (_isPlaying && !_isPausing)
        {
            _currentTime += Time.deltaTime * _playSpeed;
            TransmitOsc();
        }
    }

    private void OnDestroy()
    {
        if (_isRecording)
            StopRecording();
        if (_isPlaying)
            CloseReadFiles();
    }
    #endregion
}
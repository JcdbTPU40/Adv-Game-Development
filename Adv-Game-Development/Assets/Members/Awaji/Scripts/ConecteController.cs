using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;
using Toufuku.GameInput;
using UnityEngine;

// #51: 受信した行を同じフレームの入力処理（ThrowInputController）より先に配るため、実行順を早める
[DefaultExecutionOrder(-200)]
public class ConecteController : MonoBehaviour
{
    public string deviceID = "OONUSA_READY";

    // 1 フレームで読む最大行数（受信が詰まっても Update が止まらないようにする上限）
    const int MaxLinesPerFrame = 32;

    // 1 つのポートで ESP32 の行を待つ秒数
    const double ProbeSeconds = 2.0;

    SerialPort serial;

    public bool isConnected { get; private set; }

    /// <summary>#65: いまつながっている COM ポートの名前。つながっていなければ null。</summary>
    public string portName { get; private set; }

    /// <summary>#65: 別スレッドで COM ポートを探しなおしているか。</summary>
    public bool isScanning => _scanThread != null && _scanThread.IsAlive;

    public float yaw { get; private set; }
    public float pitch { get; private set; }
    public float roll { get; private set; }

    /// <summary>#51: 4 項目目のボタンのビットマスク（bit0〜4 = 色、bit5 = 正面）。未送信なら 0。</summary>
    public int buttons { get; private set; }
    /// <summary>#51: ファームウェアがボタンを送ってきているか。</summary>
    public bool hasButtons { get; private set; }

    /// <summary>#51: 1 行受信するたびに発火する（振りピーク検出用に全行を配る）。</summary>
    public event Action<ControllerSample> SampleReceived;

    // #65: 探しなおし（別スレッド）。見つけたポートは Update（メインスレッド）で使い始める
    Thread _scanThread;
    volatile SerialPort _scanResult;
    volatile bool _scanCancel;
    string _lastPortName;

    void Start()
    {
        Connect();
    }

    void Connect()
    {
        SerialPort found = ProbePorts(deviceID, SerialPort.GetPortNames(), null, () => false);
        if (found != null) Adopt(found);
    }

    /// <summary>
    /// #65: 通信がとぎれたときに呼ぶ（ControllerLinkSupervisor）。今のポートを閉じて、別スレッドで探しなおす（ゲームは止めない）。
    /// 見つかったら次の Update から使い始める。前につながっていたポートは最後に試すので、
    /// 無線（Bluetooth の仮想 COM ポート）が切れたあとに USB ケーブルを挿すと USB のほうを先に見つける。
    /// </summary>
    public void BeginReconnect()
    {
        AdoptScanResult();
        if (isScanning) return;

        CloseSerial();

        string id = deviceID;
        string previous = _lastPortName;
        string[] ports = SerialPort.GetPortNames();
        _scanCancel = false;
        _scanThread = new Thread(() =>
        {
            SerialPort found = ProbePorts(id, ports, previous, () => _scanCancel);
            if (found == null) return;
            if (_scanCancel) SafeClose(found);
            else _scanResult = found;
        })
        {
            IsBackground = true,
            Name = "ConecteController.Scan"
        };
        _scanThread.Start();
    }

    // ports を順番に開いて、ESP32 の行が届いたポートを開いたまま返す。見つからなければ null（別スレッドからも呼ぶ）
    static SerialPort ProbePorts(string deviceID, string[] ports, string tryLast, Func<bool> cancelled)
    {
        var order = new List<string>(ports ?? new string[0]);
        if (!string.IsNullOrEmpty(tryLast) && order.Remove(tryLast)) order.Add(tryLast);

        foreach (string name in order)
        {
            if (cancelled()) return null;

            SerialPort testPort = null;
            try
            {
                Debug.Log($"検索中 : {name}");

                testPort = new SerialPort(name, 115200);
                testPort.ReadTimeout = 1000;
                testPort.Open();

                bool found = false;
                // #65: 別スレッドからも呼ぶので、Time.realtimeSinceStartup ではなく Stopwatch で待つ
                var watch = System.Diagnostics.Stopwatch.StartNew();

                while (watch.Elapsed.TotalSeconds < ProbeSeconds && !cancelled())
                {
                    try
                    {
                        string line = testPort.ReadLine().Trim();
                        Debug.Log(line);

                        // #51: ボタン付きの 4 項目の行も ESP32 とみなす
                        if (line == deviceID || ControllerSample.TryParse(line, 0.0, out _))
                        {
                            found = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (found)
                {
                    Debug.Log($"発見！ {name}");
                    return testPort;
                }

                Debug.Log("ESP32が見つかりませんでした。");
                testPort.Close();
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
                SafeClose(testPort);
            }
        }
        return null;
    }

    void Update()
    {
        AdoptScanResult();

        if (serial == null || !serial.IsOpen)
            return;

        try
        {
            // #51: 1 フレーム 1 行だと送信周期（20ms）に追いつかず遅延が溜まるため、届いている行をすべて読む
            for (int i = 0; i < MaxLinesPerFrame && serial.BytesToRead > 0; i++)
            {
                string line = serial.ReadLine();

                if (!ControllerSample.TryParse(line, Time.realtimeSinceStartupAsDouble, out ControllerSample sample))
                    continue;

                yaw = sample.Yaw;
                pitch = sample.Pitch;
                roll = sample.Roll;
                buttons = sample.Buttons;
                hasButtons = sample.HasButtons;

                SampleReceived?.Invoke(sample);
            }
        }
        catch (TimeoutException)
        {
            // 行のとちゅうで止まった。次のフレームでまた読む
        }
        catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException)
        {
            // #65: ケーブルが抜けたなど、もう読めない。ポートを閉じて isConnected をもどす（探しなおすのは ControllerLinkSupervisor）
            Debug.LogWarning($"[ConecteController] {portName} から読めなくなりました（{e.GetType().Name}: {e.Message}）。ポートを閉じます");
            CloseSerial();
        }
        catch
        { }
    }

    void AdoptScanResult()
    {
        SerialPort found = _scanResult;
        if (found == null) return;
        _scanResult = null;
        Adopt(found);
        Debug.Log($"[ConecteController] {portName} につなぎなおしました");
    }

    void Adopt(SerialPort port)
    {
        serial = port;
        portName = port.PortName;
        _lastPortName = portName;
        isConnected = true;
    }

    void CloseSerial()
    {
        SafeClose(serial);
        serial = null;
        portName = null;
        isConnected = false;
    }

    static void SafeClose(SerialPort port)
    {
        if (port == null) return;
        try
        {
            if (port.IsOpen) port.Close();
        }
        catch { }
    }

    // #65: シーンが変わるとき（タイトルへの自動復帰など）もポートを閉じる。閉じないと次のシーンの ConecteController が開けない
    void OnDestroy()
    {
        Shutdown();
    }

    void OnApplicationQuit()
    {
        Shutdown();
    }

    void Shutdown()
    {
        _scanCancel = true;
        SafeClose(_scanResult);
        _scanResult = null;
        CloseSerial();
    }
}

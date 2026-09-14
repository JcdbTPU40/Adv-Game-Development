using System;
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

    SerialPort serial;

    public bool isConnected { get; private set; }

    public float yaw { get; private set; }
    public float pitch { get; private set; }
    public float roll { get; private set; }

    /// <summary>#51: 4 項目目のボタンのビットマスク（bit0〜4 = 色、bit5 = 正面）。未送信なら 0。</summary>
    public int buttons { get; private set; }
    /// <summary>#51: ファームウェアがボタンを送ってきているか。</summary>
    public bool hasButtons { get; private set; }

    /// <summary>#51: 1 行受信するたびに発火する（振りピーク検出用に全行を配る）。</summary>
    public event Action<ControllerSample> SampleReceived;

    void Start()
    {
        Connect();
    }

    void Connect()
    {
        string[] ports = SerialPort.GetPortNames();

        foreach (string portName in ports)
        {
            try
            {
                Debug.Log($"検索中 : {portName}");

                SerialPort testPort = new SerialPort(portName, 115200);
                testPort.ReadTimeout = 1000;
                testPort.Open();

                bool found = false;
                float start = Time.realtimeSinceStartup;

                while (Time.realtimeSinceStartup - start < 2f)
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
                    Debug.Log($"発見！ {portName}");

                    serial = testPort;
                    isConnected = true;

                    return;
                }
                else
                {
                    Debug.Log("ESP32が見つかりませんでした。");
                }

                testPort.Close();
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
        }
    }

    void Update()
    {
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
        catch
        { }
    }

    void OnApplicationQuit()
    {
        if (serial != null && serial.IsOpen)
            serial.Close();
    }
}
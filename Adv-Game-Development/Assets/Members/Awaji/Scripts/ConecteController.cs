using System;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

public class ConecteController : MonoBehaviour
{
    public string deviceID = "OONUSA_READY";

    SerialPort serial;

    public bool isConnected { get; private set; }

    public float yaw { get; private set; }
    public float pitch { get; private set; }
    public float roll { get; private set; }

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
                Debug.Log($"åüçıíÜ : {portName}");

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
                        string[] data = line.Split(',');
                        Debug.Log(line);

                        if (line == deviceID || data.Length == 3 &&float.TryParse(data[0], out _) && float.TryParse(data[1], out _) && float.TryParse(data[2], out _))
                        {
                            found = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (found)
                {
                    Debug.Log($"î≠å©ÅI {portName}");

                    serial = testPort;
                    isConnected = true;

                    return;
                }
                else
                {
                    Debug.Log("ESP32Ç™å©Ç¬Ç©ÇËÇ‹ÇπÇÒÇ≈ÇµÇΩÅB");
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
            if (serial.BytesToRead > 0)
            {
                string line = serial.ReadLine();

                string[] data = line.Split(',');

                if (data.Length == 3 &&
                    float.TryParse(data[0], out float cyaw) &&
                    float.TryParse(data[1], out float cpitch) &&
                    float.TryParse(data[2], out float croll))
                {
                    yaw = cyaw;
                    pitch = cpitch;
                    roll = croll;
                }
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
using UnityEngine;
using System.IO.Ports;
using System.Threading;

public class BNOReceiver : MonoBehaviour
{
    SerialPort serial;
    Thread thread;

    public float yaw;
    public float pitch;
    public float roll;

    public string conName;
    void Start()
    {
        serial = new SerialPort(conName, 115200);
        serial.ReadTimeout = 1000;
        serial.Open();

        thread = new Thread(ReadLoop);
        thread.Start();
    }

    void ReadLoop()
    {
        while (serial.IsOpen)
        {
            try
            {
                string line = serial.ReadLine();

                string[] data = line.Split(',');

                if (data.Length == 3)
                {
                    yaw = float.Parse(data[0]);
                    pitch = float.Parse(data[1]);
                    roll = float.Parse(data[2]);

                    Debug.Log($"{yaw}, {pitch}, {roll}");
                }
            }
            catch
            {
            }
        }
    }

    void OnApplicationQuit()
    {
        if (thread != null)
            thread.Abort();

        if (serial != null && serial.IsOpen)
            serial.Close();
    }
}
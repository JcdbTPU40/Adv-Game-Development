using System.IO.Ports;
using UnityEngine;

public class ArduinoTest : MonoBehaviour
{
    SerialPort serialPort = new SerialPort("COM3", 115200);

    public float yaw;
    public float pitch;
    public float roll;

    void Start()
    {
        serialPort.Open();
        serialPort.ReadTimeout = 50;
    }

    void Update()
    {
        if (serialPort.IsOpen)
        {
            try
            {
                string data = serialPort.ReadLine();

                string[] values = data.Split(',');

                if (values.Length == 3)
                {
                    yaw = float.Parse(values[0]);

                    pitch = float.Parse(values[1]);

                    roll = float.Parse(values[2]);
                }
            }
            catch
            {

            }
        }
    }

    private void OnDestroy()
    {
        if (serialPort.IsOpen)
        {
            serialPort.Close();
        }
    }
}
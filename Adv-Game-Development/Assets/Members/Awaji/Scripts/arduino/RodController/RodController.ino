#include <Wire.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BNO055.h>

#include "BluetoothSerial.h"

BluetoothSerial SerialBT;

Adafruit_BNO055 bno =
    Adafruit_BNO055(55);

void setup()
{
    Serial.begin(115200);

    // Bluetooth名
    SerialBT.begin("ESP32_BNO055");

    Wire.begin(21, 22);

    if(!bno.begin())
    {
        Serial.println("BNO055 ERROR");

        while(1);
    }

    delay(1000);
}

void loop()
{
    sensors_event_t event;

    bno.getEvent(&event);

    float yaw =
        event.orientation.x;

    float pitch =
        event.orientation.y;

    float roll =
        event.orientation.z;

    // Bluetooth送信
    SerialBT.print(yaw);
    SerialBT.print(",");

    SerialBT.print(pitch);
    SerialBT.print(",");

    SerialBT.println(roll);

    delay(20);
}
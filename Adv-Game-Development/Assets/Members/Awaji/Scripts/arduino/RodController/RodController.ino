#include <Wire.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BNO055.h>

Adafruit_BNO055 bno = Adafruit_BNO055(55);

void setup()
{
    Serial.begin(115200);
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

    float yaw = event.orientation.x;

    float pitch = event.orientation.y;

    float roll = event.orientation.z;

    Serial.print(yaw);
    Serial.print(",");

    Serial.print(pitch);
    Serial.print(",");

    Serial.println(roll);

    delay(50);
}
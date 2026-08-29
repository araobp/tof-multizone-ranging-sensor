#include <Wire.h>
#include <SparkFun_VL53L5CX_Library.h> 

// Instantiate the sensor object
SparkFun_VL53L5CX myImager;

// Map your board's LPn pin to Arduino Pin 4
const int LPn_PIN = 4;

void setup() {
  Serial.begin(115200);
  while (!Serial) delay(10); // Required for native Uno R4 USB stability

  Serial.println("\n========================================");
  Serial.println(" GENERIC BLACK-BOARD VL53L5CX INITIALIZER");
  Serial.println("========================================");

  delay(200);

  // STEP 1: INITIALIZE I2C AT STANDARD SPEED (100 kHz)
  Wire.begin();
  Wire.setClock(100000); 

  // STEP 2: RUN INTERNAL BUS SCANNER
  Serial.println("Scanning I2C bus for device presence...");
  bool deviceFound = false;
  byte detectedAddress = 0x00;
  byte error, address;

  for(address = 1; address < 127; address++) {
    Wire.beginTransmission(address);
    error = Wire.endTransmission();

    if (error == 0) {
      Serial.print("  --> SUCCESS: Found device at address 0x");
      if (address < 16) Serial.print("0");
      Serial.println(address, HEX);
      detectedAddress = address;
      deviceFound = true;
    }
  }

  if (!deviceFound) {
    Serial.println("\n[CRITICAL HARDWARE ERROR] No response on the I2C bus.");
    Serial.println("Action Required: Your board lacks internal I2C pull-up resistors.");
    Serial.println("Please bridge a 4.7k ohm resistor from SDA to 3.3V, and SCL to 3.3V.");
    while (1) delay(10);
  }

  // STEP 3: INITIALIZE TRAFFIC WITH DETECTED ADDRESS
  Serial.println("Cranking I2C to fast 400kHz mode...");
  Wire.setClock(400000); 

  // Pass the exact address discovered during active bus scanning
  if (myImager.begin(detectedAddress, Wire) == false) {
    Serial.println("\n[CRITICAL FIRMWARE ERROR] Address acknowledged, but firmware load failed.");
    Serial.println("This confirms severe signal degradation due to missing physical pull-up resistors.");
    while (1) delay(10);
  }

  // Set configuration metrics
  myImager.setResolution(64);       // 8x8 matrix mode
  myImager.setRangingFrequency(10);  // 10 Frames per second
  myImager.startRanging();          

  Serial.println("\n>>> SYSTEM ONLINE! Outputting 8x8 matrix:");
}

void loop() {
  if (myImager.isDataReady() == true) {
    VL53L5CX_ResultsData resultsData;
    myImager.getRangingData(&resultsData);

    Serial.println("\n======= 8x8 DISTANCE MATRIX (mm) =======");
    for (int i = 0; i < 64; i++) {
      int distance = resultsData.distance_mm[i];
      int status = resultsData.target_status[i];

      if (status == 5 || status == 9) { 
        if (distance < 10)        Serial.print("   ");
        else if (distance < 100)   Serial.print("  ");
        else if (distance < 1000)  Serial.print(" ");
        Serial.print(distance);
      } else {
        Serial.print("   X"); 
      }

      if ((i + 1) % 8 == 0) Serial.println();
      else                  Serial.print(" | ");
    }
    Serial.println("========================================");
  }
  delay(2000); 
}

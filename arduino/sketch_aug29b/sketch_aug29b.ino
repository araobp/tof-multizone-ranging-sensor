#include <Wire.h>
#include <SparkFun_VL53L5CX_Library.h> 

// Instantiate the sensor object
SparkFun_VL53L5CX myImager;

// Distance clamping range (mm)
const int MIN_DISTANCE_MM = 0;
const int MAX_DISTANCE_MM = 2000;

// Frame framing markers (Control Bytes)
const uint8_t BEGIN = 0xFE;
const uint8_t END   = 0xFF;

void setup() {
  Serial.begin(115200);
  while (!Serial) delay(10); // Required for native USB stability (e.g., Uno R4)

  // STEP 1: Wait for sensor power-on stabilization (LPn hardwired to 3.3V)
  delay(200);

  // Initialize I2C Bus at standard speed
  Wire.begin();
  Wire.setClock(100000); 

  // STEP 2: RUN INTERNAL BUS SCANNER
  bool deviceFound = false;
  byte detectedAddress = 0x00;
  byte error, address;

  for(address = 1; address < 127; address++) {
    Wire.beginTransmission(address);
    error = Wire.endTransmission();

    if (error == 0) {
      detectedAddress = address;
      deviceFound = true;
      break;
    }
  }

  if (!deviceFound) {
    while (1) delay(10);
  }

  // STEP 3: INITIALIZE TRAFFIC WITH DETECTED ADDRESS
  Wire.setClock(400000); 

  // Pass the exact address discovered during active bus scanning
  if (myImager.begin(detectedAddress, Wire) == false) {
    while (1) delay(10);
  }

  // Set configuration metrics
  myImager.setResolution(64);        // 8x8 matrix mode
  myImager.setRangingFrequency(10);  // 10 Frames per second
  myImager.startRanging();          
}

void loop() {
  if (myImager.isDataReady() == true) {
    VL53L5CX_ResultsData resultsData;
    myImager.getRangingData(&resultsData);

    // Temp buffer for 64 mapped pixel values
    byte frameBuffer[64];

    for (int i = 0; i < 64; i++) {
      int distance = resultsData.distance_mm[i];
      
      int status = resultsData.target_status[i];
      int outputDistance;

      if (status == 5 || status == 9) { 
        outputDistance = constrain(distance, MIN_DISTANCE_MM, MAX_DISTANCE_MM);
      } else {
        outputDistance = MAX_DISTANCE_MM; 
      }

      // Map range: MIN_DISTANCE_MM (0mm) -> 0, MAX_DISTANCE_MM (2000mm) -> 200
      int mappedVal = map(outputDistance, MIN_DISTANCE_MM, MAX_DISTANCE_MM, 0, 200);
      frameBuffer[i] = (byte)constrain(mappedVal, 0, 200);
    }

    // Write START marker (0xFE)
    Serial.write(BEGIN);

    // Output 64 bytes aligned with sketch_amg8833.ino spatial orientation (Vertical & Horizontal flip correction)
    for (int i = 0; i < 64; i++) {
      int row = i / 8;
      int col = i % 8;
      int idx = (7 - row) * 8 + (7 - col); 
      Serial.write(frameBuffer[idx]);
    }

    // Write END marker (0xFF)
    Serial.write(END);

    // 100ms cycle interval control
    delay(100);
  }
}
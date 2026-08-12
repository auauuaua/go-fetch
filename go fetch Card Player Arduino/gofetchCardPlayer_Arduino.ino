/*
 * QR Code + IR Remote Reader
 *
 * Reads QR codes via a Tiny Code Reader (I2C) and IR remote signals.
 * Outputs decoded data over Serial in the format:
 *   QRR:<content>   — on a new QR scan
 *   IR:<hex_code>   — on an IR button press
 *   ejected         — when the card/trigger is removed
 *
 * Hardware:
 *   - Tiny Code Reader on SDA=6, SCL=7 (I2C)
 *   - Card detect switch on pin 4 (INPUT_PULLUP, active LOW)
 *   - QR Illumination LED on pin 1
 *   - IR receiver on pin 2
 *
 * Dependencies: IRremote, tiny_code_reader
 * 
 * Note if using second I2C connection like the stemma connector on Adafruit QTPY rp2040, 
 * you will need to change all instances of Wire to Wire1 (except the #include <Wire.h> lines)in this file and in tiny_code_reader.h
 * and of course pin numbers as needed
 */

#include <Wire.h>
#include <IRremote.hpp>
#include "tiny_code_reader.h"

// --- Pin assignments ---
const int PIN_CARD_DETECT = 4;
const int PIN_ILLUM_LED  = 1;
const int PIN_IR_RECEIVER = 2;

// --- Timing ---
const int32_t  SAMPLE_DELAY_MS  = 200;  // Sensor updates at ~5 FPS
const uint32_t DEBOUNCE_DELAY_MS = 200; // Minimum ms between repeated IR signals

// --- State ---
char     lastScannedCode[128] = "";
bool     cardInserted         = false;
uint32_t lastIRCode           = 0;
uint32_t lastIRTime           = 0;

void setup() {
  Wire.setSDA(6);
  Wire.setSCL(7);
  Wire.begin();
  Serial.begin(9600);

  pinMode(PIN_CARD_DETECT, INPUT_PULLUP);
  pinMode(PIN_ILLUM_LED,  OUTPUT);
  IrReceiver.begin(PIN_IR_RECEIVER);
}

void loop() {
  tiny_code_reader_results_t results = {};

  if (!tiny_code_reader_read(&results)) {
    Serial.println("No person sensor results found on the i2c bus");
    delay(SAMPLE_DELAY_MS);
    return;
  }

  // --- Card detect ---
  bool cardPresent = (digitalRead(PIN_CARD_DETECT) == LOW);

  if (cardPresent) {
      if (!cardInserted) {
          digitalWrite(PIN_ILLUM_LED, HIGH);
  
          if (results.content_length > 0) {
              char* scanned = (char*)results.content_bytes;
  
              if (strcmp(scanned, lastScannedCode) != 0) {
                  strncpy(lastScannedCode, scanned, sizeof(lastScannedCode) - 1);
                  lastScannedCode[sizeof(lastScannedCode) - 1] = '\0';
  
                  Serial.print("QRR:");
                  Serial.println(lastScannedCode);
  
                  digitalWrite(PIN_ILLUM_LED, LOW);
                  cardInserted = true;
              }
          }
      }
  } else {
    if (cardInserted) {
      Serial.println("ejected");
    }
    cardInserted        = false;
    lastScannedCode[0]  = '\0';
    digitalWrite(PIN_ILLUM_LED, LOW);
  }

  // --- IR receiver ---
  if (IrReceiver.decode()) {
    if (IrReceiver.decodedIRData.protocol != UNKNOWN) {
      uint32_t now = millis();

      if (IrReceiver.decodedIRData.flags & IRDATA_FLAGS_IS_REPEAT) {
        if (now - lastIRTime >= DEBOUNCE_DELAY_MS) {
          Serial.print("IR:");
          Serial.println(lastIRCode, HEX);
          lastIRTime = now;
        }
      } else {
        lastIRCode = IrReceiver.decodedIRData.decodedRawData;
        Serial.print("IR:");
        Serial.println(lastIRCode, HEX);
        lastIRTime = now;
      }
    }
    IrReceiver.resume();
  }

  delay(SAMPLE_DELAY_MS);
}

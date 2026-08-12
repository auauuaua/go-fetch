# Go Fetch Card Player - Arduino

Firmware for the Go Fetch Card Player, running on a Seeed Studio XIAO RP2040. Reads QR codes from physical cards via a Tiny Code Reader (I2C) and receives IR remote signals, reporting both over Serial.

---

## Hardware

| Component | Details |
|---|---|
| Microcontroller | Seeed Studio XIAO RP2040 |
| QR Scanner | Tiny Code Reader (Useful Sensors) — I2C |
| IR Receiver | Connected to pin D2 |
| Card Detect Switch | Connected to pin D4 (active LOW, INPUT_PULLUP) |
| Illumination LED | For QR code reader lighting, connected to pin D1 |

### Wiring

| XIAO RP2040 Pin | Connected To |
|---|---|
| D6 (SDA) | Tiny Code Reader SDA |
| D7 (SCL) | Tiny Code Reader SCL |
| D4 | Card detect switch (other leg to GND) |
| D1 | QR illumination LED (with appropriate resistor) |
| D2 | IR receiver signal pin |
| 3.3V / GND | Tiny Code Reader power |

---

## Dependencies

| Library | Version | License |
|---|---|---|
| [IRremote](https://github.com/Arduino-IRremote/Arduino-IRremote) | 4.x | MIT / LGPL |
| [tiny_code_reader](https://github.com/usefulsensors/tiny_code_reader_arduino) | — | Apache 2.0 |

Install both via the Arduino Library Manager, or clone them into your `Arduino/libraries/` folder.

---

## How It Works

1. A card is inserted, closing the card detect switch (pin D4 pulled LOW).
2. The illumination LED turns on to light the QR code for the reader.
3. The Tiny Code Reader scans the card's QR code over I2C.
4. On a successful new scan, the result is printed over Serial as `QRR:<content>` and the illumination LED turns off.
5. The card can remain inserted without re-triggering; removing it prints `ejected` over Serial and resets the state.
6. IR remote signals are received independently and printed as `IR:<hex_code>`.

### Serial Output Format

| Event | Output |
|---|---|
| QR code scanned | `QRR:<decoded content>` |
| IR button pressed | `IR:<hex code>` |
| Card removed | `ejected` |

Serial baud rate: **9600**

---

## Building & Flashing

1. Open `CardPlayer_Arduino.ino` in the Arduino IDE.
2. Under **Tools > Board**, select **Seeed XIAO RP2040**.
3. Install the dependencies listed above.
4. Connect the XIAO via USB and select the correct port.
5. Click **Upload**. The board will reboot and appear as a USB drive briefly during flashing.

---

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

Third-party libraries retain their own licenses — see the `LICENSE` file in each library's folder under `Arduino/libraries/`.

---

## Acknowledgements

- [IRremote](https://github.com/Arduino-IRremote/Arduino-IRremote) by shirriff, z3t0, ArminJo et al.
- [tiny_code_reader](https://github.com/usefulsensors/tiny_code_reader_arduino) by Useful Sensors / Pete Warden

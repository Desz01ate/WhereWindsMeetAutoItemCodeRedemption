# Where Winds Meet Auto Item Code Redemption

## English

A Windows-only Python utility that finds active **Where Winds Meet** redemption codes from configured web pages and APIs, then enters them into the currently visible game window through mouse and keyboard input.

### Features

- Collects codes from HTML sources and JSON APIs.
- Removes duplicate codes and ignores codes recorded as redeemed.
- Defaults to preview mode; it does not send input unless `--execute` is provided.
- Saves successful redemptions to `redeemed_codes.json`.
- Supports one-code runs, PID selection, coordinate calibration, and target inspection.

### Requirements

- Windows, because the automation uses the Windows User32 API.
- Python 3.9 or newer.
- **Where Winds Meet** running in a visible window.
- Network access to the configured code sources.

The project uses only Python standard-library modules; no package installation is required.

### Quick start

1. Open the game and leave its redemption interface available in a visible window.
2. Review `config.json`, especially the game process name and UI coordinates.
3. Preview discovered codes without sending any game input:

   ```bat
   python redeem_codes.py --config config.json
   ```

4. After checking the preview, execute the redemption flow:

   ```bat
   python redeem_codes.py --config config.json --execute
   ```

The included launcher can be used from Windows:

```bat
run_redeemer.bat --execute
```

The script asks for confirmation after each submission. Press **C** only when the game shows that redemption succeeded; press **Q** to leave the code pending so it can be retried later.

### Command-line options

| Option | Description |
| --- | --- |
| `--config PATH` | Configuration file; defaults to `config.json`. |
| `--execute` | Actually send mouse and keyboard input. Without it, the run is preview-only. |
| `--once` | Stop after one newly found code. |
| `--pid PID` | Override the process ID for this run. |
| `--calibrate` | Hover over configured UI targets and press **C** to save normalized coordinates. |
| `--show-targets` | Move the cursor to configured targets without clicking. |
| `--space-fallback` | Use Space for confirmation only when `submit_button` is not calibrated. |

`--calibrate` and `--show-targets` cannot be used together.

### Configuration

`config.json` contains:

- `process_name`: executable name used to locate the game window.
- `state_file`: path to the local redeemed-code state file.
- `sources`: HTML pages to scan.
- `api_sources`: JSON endpoints whose `active` entries contain codes.
- `timing`: page timeout, UI delay, and result wait values.
- `ui`: normalized client-area coordinates for the exchange button, code input, submit button, and cancel button.

Coordinates are normalized from `0.0` to `1.0` relative to the game client area. Use calibration if the game window layout or resolution changes:

```bat
python redeem_codes.py --config config.json --calibrate
```

To inspect the configured positions without clicking:

```bat
python redeem_codes.py --config config.json --show-targets
```

### State and safety

`redeemed_codes.json` is local runtime state and is ignored by Git. The program adds a code to that file only after you confirm successful redemption. Do not use `--execute` until the game window is focused correctly and the configured targets have been checked. The tool sends external input to the selected visible window; it does not control game accounts or bypass game protections.

### Updating code sources

Edit the `sources` and `api_sources` arrays in `config.json`. HTML scraping accepts explicit `value`/`data-code` attributes and codes in the first cell of two-column table rows. API scraping reads the `active` array and its `code` fields. Unavailable sources are reported as warnings and do not stop the remaining sources from being processed.

---

## ภาษาไทย

ยูทิลิตี Python สำหรับ Windows ที่ค้นหาโค้ดแลกรางวัล **Where Winds Meet** ที่ยังใช้งานได้จากหน้าเว็บและ API ที่กำหนดไว้ จากนั้นกรอกโค้ดลงในหน้าต่างเกมที่มองเห็นอยู่ผ่านการควบคุมเมาส์และคีย์บอร์ด

### ความสามารถ

- รวบรวมโค้ดจากแหล่งข้อมูล HTML และ JSON API
- ลบโค้ดซ้ำ และข้ามโค้ดที่บันทึกว่าแลกไปแล้ว
- ทำงานในโหมดแสดงตัวอย่างเป็นค่าเริ่มต้น จะไม่ส่งอินพุตให้เกมหากไม่ระบุ `--execute`
- บันทึกโค้ดที่แลกสำเร็จไว้ใน `redeemed_codes.json`
- รองรับการทำงานทีละหนึ่งโค้ด การระบุ PID การปรับเทียบพิกัด และการตรวจสอบตำแหน่งเป้าหมาย

### สิ่งที่ต้องมี

- Windows เนื่องจากระบบอัตโนมัติใช้ Windows User32 API
- Python 3.9 ขึ้นไป
- เปิด **Where Winds Meet** ไว้ในหน้าต่างที่มองเห็นได้
- การเชื่อมต่อเครือข่ายไปยังแหล่งโค้ดที่กำหนดไว้

โปรเจกต์นี้ใช้เฉพาะไลบรารีมาตรฐานของ Python จึงไม่ต้องติดตั้งแพ็กเกจเพิ่มเติม

### เริ่มใช้งานอย่างรวดเร็ว

1. เปิดเกมและเปิดหน้าสำหรับแลกรางวัลไว้ในหน้าต่างที่มองเห็นได้
2. ตรวจสอบ `config.json` โดยเฉพาะชื่อโปรเซสของเกมและพิกัดส่วนติดต่อผู้ใช้
3. แสดงโค้ดที่ค้นพบโดยยังไม่ส่งอินพุตใด ๆ ให้เกม:

   ```bat
   python redeem_codes.py --config config.json
   ```

4. หลังตรวจสอบผลลัพธ์แล้ว จึงเริ่มขั้นตอนการแลกโค้ด:

   ```bat
   python redeem_codes.py --config config.json --execute
   ```

สามารถใช้ไฟล์เรียกใช้งานที่มีมาให้บน Windows ได้เช่นกัน:

```bat
run_redeemer.bat --execute
```

หลังส่งโค้ดแต่ละรายการ สคริปต์จะรอการยืนยันจากผู้ใช้ กด **C** เมื่อเกมแสดงว่าแลกสำเร็จเท่านั้น หรือกด **Q** เพื่อคงโค้ดนั้นไว้สำหรับลองใหม่ภายหลัง

### ตัวเลือกบรรทัดคำสั่ง

| ตัวเลือก | คำอธิบาย |
| --- | --- |
| `--config PATH` | ไฟล์ตั้งค่า ค่าเริ่มต้นคือ `config.json` |
| `--execute` | ส่งอินพุตเมาส์และคีย์บอร์ดจริง หากไม่ระบุจะเป็นโหมดแสดงตัวอย่าง |
| `--once` | หยุดหลังพบโค้ดใหม่หนึ่งรายการ |
| `--pid PID` | ระบุ process ID สำหรับการทำงานครั้งนี้ |
| `--calibrate` | เลื่อนเมาส์ไปยังเป้าหมายและกด **C** เพื่อบันทึกพิกัดแบบ normalized |
| `--show-targets` | เลื่อนเมาส์ไปยังเป้าหมายโดยไม่คลิก |
| `--space-fallback` | ใช้ Space เพื่อยืนยันเฉพาะเมื่อยังไม่ได้ปรับเทียบ `submit_button` |

ไม่สามารถใช้ `--calibrate` และ `--show-targets` พร้อมกันได้

### การตั้งค่า

ไฟล์ `config.json` ประกอบด้วย:

- `process_name`: ชื่อไฟล์ executable ที่ใช้ค้นหาหน้าต่างเกม
- `state_file`: ตำแหน่งไฟล์สถานะโค้ดในเครื่อง
- `sources`: หน้า HTML ที่จะสแกน
- `api_sources`: จุดเชื่อมต่อ JSON ที่มีโค้ดอยู่ในรายการ `active`
- `timing`: ค่าหมดเวลาของหน้าเว็บ ระยะหน่วง UI และเวลารอผลลัพธ์
- `ui`: พิกัดแบบ normalized ในพื้นที่ client สำหรับปุ่มแลก ช่องกรอกโค้ด ปุ่มส่ง และปุ่มยกเลิก

พิกัดมีค่าตั้งแต่ `0.0` ถึง `1.0` โดยอ้างอิงจากพื้นที่ client ของเกม หากขนาดหน้าต่างหรือความละเอียดเปลี่ยน ให้ปรับเทียบใหม่:

```bat
python redeem_codes.py --config config.json --calibrate
```

หากต้องการตรวจสอบตำแหน่งที่ตั้งค่าไว้โดยไม่คลิก:

```bat
python redeem_codes.py --config config.json --show-targets
```

### สถานะและความปลอดภัย

`redeemed_codes.json` เป็นไฟล์สถานะ runtime ในเครื่องและถูกตั้งค่าให้ Git มองข้าม โปรแกรมจะเพิ่มโค้ดลงไฟล์นี้หลังจากผู้ใช้ยืนยันว่าแลกสำเร็จเท่านั้น อย่าใช้ `--execute` จนกว่าจะตรวจสอบว่าหน้าต่างเกมและพิกัดเป้าหมายถูกต้อง เครื่องมือนี้ส่งอินพุตภายนอกไปยังหน้าต่างที่เลือกเท่านั้น ไม่ได้ควบคุมบัญชีเกมหรือหลีกเลี่ยงระบบป้องกันของเกม

### การแก้ไขแหล่งโค้ด

แก้ไขรายการ `sources` และ `api_sources` ใน `config.json` การสแกน HTML รองรับแอตทริบิวต์ `value`/`data-code` ที่ระบุโค้ดโดยตรง และโค้ดในช่องแรกของแถวตารางสองคอลัมน์ การอ่าน API จะใช้รายการ `active` และฟิลด์ `code` แหล่งข้อมูลที่ไม่พร้อมใช้งานจะแสดงคำเตือน แต่ไม่ทำให้การประมวลผลแหล่งข้อมูลอื่นหยุดลง

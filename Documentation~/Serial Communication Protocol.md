# Serial Communication Protocol

## Frame Format
 
| Field | Length (Bytes) | Description |
|--------------|--------------|----------------------|
| Frame Header | 2 | Fixed value used to mark the start of a data frame. |
| Command/Msg Type | 1 | Defines the function of this packet, whether it is a command or a response. |
| Data Length | 1 | Byte length of the Data Payload field. |
| Data Payload | 0 - 255 | Actual data content being transmitted, variable length. |
| Checksum | 1 | Used to verify data integrity and prevent transmission errors. |
| Frame Tail | 2 | Fixed value used to mark the end of a data frame. |


## Frame Details

- Frame Header: 0xAA 0x55

- Command/Msg Type
  - PC -> UMH (Command)
    - 0x01: Point Info (Get or set information of a specific point)
    - 0x02: Enable/Disable (Enable or disable the device)
    - 0x03: GetStatus (Get device status)
    - 0x04: Ping (Used for connection test and automatic serial port recognition)
  - UMH -> PC (Response)
    - 0x80: ACK (General success response)
    - 0x81: NACK (General failure response)
    - 0x82: Return Status (Return specific status, e.g., temperature)
    - 0x83: Ping_ACK (Specific response to Ping command)
      - For the Ping command (0x04): The PC may send a random number as the data payload, and the UMH must return that same random number unchanged in the Ping_ACK (0x83) response to increase recognition reliability.
    - 0xFF: Error Code (Return detailed error code)
- Data Length: unsigned char (1 byte)
  - Type: unsigned char (1 byte)
  - Range: 0 - 255
  - Description: The value of this byte indicates how many bytes are in the Data Payload that follows. If a command has no data payload (e.g., GetVersion), this byte is 0x00
- Data Payload
  - This field carries the actual data, the content of which depends on the Command/Msg Type.
- Checksum
  - Algorithm: 8-bit Sum Checksum
  - Calculation: Perform unsigned addition on all bytes from Command/Msg Type through the end of Data Payload, then take the lowest 8 bits of the result (i.e., modulo 256).
  - Advantages: Simple to compute, minimal resource usage, ideal for MCUs like STM32, and effectively detects single-byte transmission errors.
  - Example:
    - Command: GetVersion (0x04), no data payload.
    - Bytes to checksum: [Command/Msg Type] [Data Length] [Data Payload]
    - That is: 0x04 + 0x00 = 0x04
    - The checksum byte is therefore 0x04.

- Frame Tail: 0x0D 0x0A
  - Type: unsigned char (2 bytes)
  - Description: Fixed value used to mark the end of a data frame.

GPM4 NAND Dumper Automator
==========================

This program is to be used with GPM4 NAND Dumper Host for dumping NAND from
a GPM4 system while it is in bootrom.

Usage
-----
1. Change parameters at the top of `Program.cs` to match your NAND chip. Set
   `basePath` to the directory you want to use as OpenOCD's working directory.
2. Clone and build the host into `nand_dump_host` in `basePath`.
3. Start OpenOCD with your target attached, using `basePath` as the working
   directory.
4. Run this program; it will dump the NAND from the system and stitch it
   together as `nand_dump_from_device.bin` under `basePath`.

Boot header vs normal BCH
-------------------------
Boot header area may use smaller pages and spare than for the rest of the NAND.
Adjust the address on the indicated line to break before or after bootloader
search begins to select boot header page/spare size or regular page/spare size.
Error correction will not work if the page/spare size of the pages you are
interested in do not match what is configured at the moment.

Note that error correction data is not error corrected in itself when dumped.
You may have to dump multiple times to confirm that the correct spare bits are
present.

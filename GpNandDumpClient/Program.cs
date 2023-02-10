// See https://aka.ms/new-console-template for more information

using PrimS.Telnet;

const uint LOAD_ADDR = 0x01fff000;
const uint BP_ADDR = LOAD_ADDR + 0x84;
const uint CTX_ADDR = LOAD_ADDR + 0xa0;

const uint PAGE_SIZE = 4096;
const uint SPARE_SIZE = 256;
const uint SPARE_USED_SIZE = 0xc0;
const uint PAGE_SIZE_TOTAL = PAGE_SIZE + SPARE_SIZE;
const uint NUM_PAGES_PER_RUN = 7695;
const uint MAX_PAGES = 131072;
const uint START_PAGE = 0;

string basePath = @"C:\projects\leapland_adventures";

using var client = new Client("127.0.0.1", 4444, new CancellationToken());

async Task<string> WaitForPrompt(string prompt = "> ")
{
    string response = await client.TerminatedReadAsync(prompt, TimeSpan.FromMinutes(5));
    Console.Write(response);
    return response;
}

async Task WaitForHalt()
{
    await WaitForPrompt("target halted");
}

async Task<string> SendCommandAndWait(string command)
{
    await client.WriteLineAsync(command);
    return await WaitForPrompt();
}

Directory.CreateDirectory(Path.Combine(basePath, "nand_dump_chunks"));

await WaitForPrompt();

// Set to false to skip init if already ran a session
if (true)
{
    // Init NAND controller and SDRAM init
    await SendCommandAndWait("halt");
    await SendCommandAndWait("adapter speed 15000");
    await SendCommandAndWait("rbp all");
    // Set to regular page/spare size; use 0x1ff817ec for boot header page/spare size
    await SendCommandAndWait("bp 0x1ff817f0 2 hw");
    await SendCommandAndWait("reg pc 0x1ff82a84");
    await SendCommandAndWait("resume");
    await WaitForHalt();

    // Upload dumper host
    await SendCommandAndWait($"load_image nand_dump_host/convert.bin 0x{LOAD_ADDR:x8}");
    await SendCommandAndWait($"bp 0x{BP_ADDR:x8} 2 hw");
    await SendCommandAndWait($"mww 0x{CTX_ADDR:x8} 0x0");
    await SendCommandAndWait($"mww 0x{CTX_ADDR + 4:x8} 0x{PAGE_SIZE:x8}");
    await SendCommandAndWait($"mww 0x{CTX_ADDR + 8:x8} 0x{SPARE_SIZE:x8}");
    await SendCommandAndWait($"mww 0x{CTX_ADDR + 12:x8} 0x{NUM_PAGES_PER_RUN:x8}");
    await SendCommandAndWait($"mww 0x{CTX_ADDR + 16:x8} 0x0");
    await SendCommandAndWait($"reg pc 0x{LOAD_ADDR:x8}");
    await client.WriteLineAsync("resume");
    await WaitForHalt();
}

async Task PerformDump(string path, uint startPage, uint endPage)
{
    await SendCommandAndWait($"mww 0x{CTX_ADDR:x8} 0x{startPage:x8}");
    await SendCommandAndWait($"mww 0x{CTX_ADDR + 16:x8} 0x{endPage:x8}");

    using (FileStream fs = File.Create(path))
    {
        int i = 0;
        for (uint pageNum = startPage; pageNum < endPage; pageNum += NUM_PAGES_PER_RUN)
        {
            while (pageNum < START_PAGE)
            {
                fs.Write(new byte[PAGE_SIZE_TOTAL]);
                ++pageNum;
            }

            int current = i * (int)NUM_PAGES_PER_RUN;
            int total = (int)(endPage - startPage);
            Console.WriteLine($"Dumping {current} of {total} ({(float)current / total:P})");
            await client.WriteLineAsync("resume");
            await WaitForHalt();
            uint pagesToRead = endPage - pageNum;
            if (pagesToRead > NUM_PAGES_PER_RUN) pagesToRead = NUM_PAGES_PER_RUN;
            await SendCommandAndWait($"dump_image nand_dump_chunks/{i}.bin 0x0 {pagesToRead * PAGE_SIZE_TOTAL}");
            using (var chunkFs = File.OpenRead(Path.Combine(basePath, "nand_dump_chunks", $"{i}.bin")))
            {
                chunkFs.CopyTo(fs);
            }
            ++i;
        }

        // Fix spare data when data is less than size of spare
        if (SPARE_USED_SIZE < SPARE_SIZE)
        {
            byte[] spareRem = new byte[SPARE_SIZE - SPARE_USED_SIZE];
            ((Span<byte>)spareRem).Fill(0xff);
            fs.Seek(0, SeekOrigin.Begin);
            while (fs.Position < fs.Length)
            {
                fs.Seek(PAGE_SIZE, SeekOrigin.Current);
                byte isBadPage = (byte)fs.ReadByte();
                fs.Seek(SPARE_USED_SIZE - 1, SeekOrigin.Current);
                if (isBadPage == 0x00)
                    fs.Write(new byte[spareRem.Length]);
                else
                    fs.Write(spareRem);
            }
            fs.Flush();
        }
    }
}

await PerformDump(Path.Combine(basePath, "nand_dump_from_device.bin"), START_PAGE, MAX_PAGES);

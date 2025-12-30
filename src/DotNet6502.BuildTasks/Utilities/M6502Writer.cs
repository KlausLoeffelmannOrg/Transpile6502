using System.Buffers;
using System.Text;

namespace DotNet6502;

/// <summary>
/// Writes .nes files
/// * https://wiki.nesdev.org/w/index.php/INES
/// * https://bheisler.github.io/post/nes-rom-parser-with-nom/
/// </summary>
class M6502Writer : IDisposable
{
    public static readonly Encoding Encoding = Encoding.ASCII;

    public M6502Writer(Stream stream, bool leaveOpen = false, ILogger? logger = null)
    {
        _writer = new(stream, Encoding, leaveOpen);
        _logger = logger ?? new NullLogger();

        // NOTE: starting values so they exist in dictionary
        Labels[nameof(Copydata)] = 0;
        Labels[nameof(popa)] = 0;
        Labels[nameof(popax)] = 0;
        Labels[nameof(pusha)] = 0;
        Labels[nameof(pushax)] = 0;
        Labels[nameof(zerobss)] = 0;
        Labels[nameof(rodata)] = 0;
        Labels[nameof(donelib)] = 0;
    }

    /// <summary>
    /// PRG_ROM is in 16 KB units
    /// </summary>
    public const int PRG_ROM_BLOCK_SIZE = 16384;
    /// <summary>
    /// CHR ROM in in 8 KB units
    /// </summary>
    public const int CHR_ROM_BLOCK_SIZE = 8192;

    protected const int ZP_START = 0x00;
    protected const int STARTUP = 0x01;
    protected const int NES_PRG_BANKS = 0x02;
    protected const int VRAM_UPDATE = 0x03;
    protected const int NAME_UPD_ADR = 0x04;
    protected const int NAME_UPD_ENABLE = 0x06;
    protected const int PAL_UPDATE = 0x07;
    protected const int PAL_BG_PTR = 0x08;
    protected const int PAL_SPR_PTR = 0x0A;
    protected const int SCROLL_X = 0x0C;
    protected const int SCROLL_Y = 0x0D;
    protected const int TEMP = 0x17;
    protected const int sp = 0x22;
    protected const int ptr1 = 0x2A;
    protected const int ptr2 = 0x2C;
    protected const int tmp1 = 0x32;
    protected const int PRG_FILEOFFS = 0x10;
    protected const int PPU_MASK_VAR = 0x12;
    protected const ushort OAM_BUF = 0x0200;
    protected const ushort PAL_BUF = 0x01C0;
    protected const ushort Condes = 0x0300;
    protected const ushort PPU_CTRL = 0x2000;
    protected const ushort PPU_MASK = 0x2001;
    protected const ushort PPU_STATUS = 0x2002;
    protected const ushort PPU_OAM_ADDR = 0x2003;
    protected const ushort PPU_OAM_DATA = 0x2004;
    protected const ushort PPU_SCROLL = 0x2005;
    protected const ushort PPU_ADDR = 0x2006;
    protected const ushort PPU_DATA = 0x2007;
    protected const ushort DMC_FREQ = 0x4010;
    protected const ushort PPU_OAM_DMA = 0x4014;
    protected const ushort PPU_FRAMECNT = 0x4017;
    protected const ushort skipNtsc = 0x81F9;
    protected const ushort pal_col = 0x823E;
    protected const ushort vram_adr = 0x83D4;
    protected const ushort vram_write = 0x834F;
    protected const ushort ppu_on_all = 0x8289;
    protected const ushort ppu_wait_nmi = 0x82F0;
    protected const ushort updName = 0x8385;
    protected const ushort palBrightTableL = 0x8422;
    protected const ushort palBrightTableH = 0x842B;

    // Post-main functions
    protected const ushort Copydata = 0x850C;
    protected const ushort popa = 0x854F;
    protected const ushort popax = 0x8539;
    protected const ushort pusha = 0x855F;
    protected const ushort pushax = 0x8575;
    protected const ushort zerobss = 0x858B;
    protected const ushort rodata = 0x85AE;
    protected const ushort donelib = 0x84FD;

    protected const ushort BaseAddress = 0x8000;

    protected readonly BinaryWriter _writer;
    protected readonly ILogger _logger;

    public bool LastLDA { get; protected set; }

    public Stream BaseStream => _writer.BaseStream;

    /// <summary>
    /// Trainer, if present (0 or 512 bytes)
    /// </summary>
    public byte[]? Trainer { get; set; }

    /// <summary>
    /// PRG ROM data (16384 * x bytes)
    /// </summary>
    public byte[]? PRG_ROM { get; set; }

    /// <summary>
    /// CHR ROM data, if present (8192 * y bytes)
    /// </summary>
    public byte[]? CHR_ROM { get; set; }

    /// <summary>
    /// PlayChoice INST-ROM, if present (0 or 8192 bytes)
    /// </summary>
    public byte[]? INST_ROM { get; set; }

    /// <summary>
    /// Mapper, mirroring, battery, trainer
    /// </summary>
    public byte Flags6 { get; set; }

    /// <summary>
    /// Mapper, VS/Playchoice, NES 2.0
    /// </summary>
    public byte Flags7 { get; set; }

    /// <summary>
    /// PRG-RAM size (rarely used extension)
    /// </summary>
    public byte Flags8 { get; set; }

    /// <summary>
    /// TV system (rarely used extension)
    /// </summary>
    public byte Flags9 { get; set; }

    /// <summary>
    /// TV system, PRG-RAM presence (unofficial, rarely used extension)
    /// </summary>
    public byte Flags10 { get; set; }

    public long Length => _writer.BaseStream.Length;

    public Dictionary<string, ushort> Labels { get; private set; } = new();
    private bool _hasPresetLabels = false;

    public void SetLabels(Dictionary<string, ushort> labels)
    {
        Labels = labels;
        _hasPresetLabels = true;
    }

    /// <summary>
    /// A list of methods that were found to be used in the IL code
    /// </summary>
    public HashSet<string>? UsedMethods { get; set; }

    public void WriteHeader(byte PRG_ROM_SIZE = 0, byte CHR_ROM_SIZE = 0)
    {
        _writer.Write('N');
        _writer.Write('E');
        _writer.Write('S');
        _writer.Write('\x1A');
        // Size of PRG ROM in 16 KB units
        if (PRG_ROM != null)
            _writer.Write(checked ((byte)(PRG_ROM.Length / PRG_ROM_BLOCK_SIZE)));
        else
            _writer.Write(PRG_ROM_SIZE);
        // Size of CHR ROM in 8 KB units (Value 0 means the board uses CHR RAM)
        if (CHR_ROM != null)
            _writer.Write(checked((byte)(CHR_ROM.Length / CHR_ROM_BLOCK_SIZE)));
        else
            _writer.Write(CHR_ROM_SIZE);
        _writer.Write(Flags6);
        _writer.Write(Flags7);
        _writer.Write(Flags8);
        _writer.Write(Flags9);
        _writer.Write(Flags10);
        // 5 bytes of padding
        WriteZeroes(5);
    }

    /// <summary>
    /// Writes N zero-d bytes
    /// </summary>
    public void WriteZeroes(long length)
    {
        for (long i = 0; i < length; i++)
        {
            _writer.Write((byte)0);
        }
    }

    public void Write(byte[] buffer)
    {
        LastLDA = false;
        _writer.Write(buffer);
    }

    public void Write(ushort[] buffer)
    {
        LastLDA = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            _writer.Write(buffer[i]);
        }
    }

    public void Write(byte[] buffer, int index, int count)
    {
        LastLDA = false;
        _writer.Write(buffer, index, count);
    }

    /// <summary>
    /// Writes a string in ASCI form, including a trailing \0
    /// </summary>
    public void WriteString(string text)
    {
        LastLDA = false;
        int length = Encoding.GetByteCount(text);
        var bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            length = Encoding.GetBytes(text, 0, text.Length, bytes, 0);
            _writer.Write(bytes, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
        //TODO: I don't know if there is a 0 between each string, or if this denotes the end of the table
        _writer.Write((byte)0);
    }

    public void WriteDestructorTable()
    {
        /*
         * 8602	8D0E03        	STA $030E                     ; __DESTRUCTOR_TABLE__
         * 8605	8E0F03        	STX $030F
         * 8608	8D1503        	STA $0315
         * 860B	8E1603        	STX $0316
         * 860E	88            	DEY
         * 860F	B9FFFF        	LDA $FFFF,y
         * 8612	8D1F03        	STA $031F
         * 8615	88            	DEY
         * 8616	B9FFFF        	LDA $FFFF,y
         * 8619	8D1E03        	STA $031E
         * 861C	8C2103        	STY $0321
         * 861F	20FFFF        	JSR $FFFF
         * 8622	A0FF          	LDY #$FF
         * 8624	D0E8          	BNE $860E
         * 8626	60            	RTS
         */
        Write(M6502Instruction.STA_abs, 0x030E);
        Write(M6502Instruction.STX_abs, 0x030F);
        Write(M6502Instruction.STA_abs, 0x0315);
        Write(M6502Instruction.STX_abs, 0x0316);
        Write(M6502Instruction.DEY_impl);
        Write(M6502Instruction.LDA_abs_y, 0xFFFF);
        Write(M6502Instruction.STA_abs, 0x031F);
        Write(M6502Instruction.DEY_impl);
        Write(M6502Instruction.LDA_abs_y, 0xFFFF);
        Write(M6502Instruction.STA_abs, 0x031E);
        Write(M6502Instruction.STY_abs, 0x0321);
        Write(M6502Instruction.JSR, 0xFFFF);
        Write(M6502Instruction.LDY, 0xFF);
        Write(M6502Instruction.BNE_rel, 0xE8);
        Write(M6502Instruction.RTS_impl);
    }

    void Write_popax()
    {
        SetLabel(nameof(popax), (ushort)(_writer.BaseStream.Position + BaseAddress));
        /*
         * 857F	A001          	LDY #$01                      ; popax
         * 8581	B122          	LDA (sp),y
         * 8583	AA            	TAX
         * 8584	88            	DEY
         * 8585	B122          	LDA (sp),y
         */
        Write(M6502Instruction.LDY, 0x01);
        Write(M6502Instruction.LDA_ind_Y, sp);
        Write(M6502Instruction.TAX_impl);
        Write(M6502Instruction.DEY_impl);
        Write(M6502Instruction.LDA_ind_Y, sp);
    }

    void Write_incsp2()
    {
        /*
        * 8587	E622          	INC sp                        ; incsp2
        * 8589	F005          	BEQ $8590
        * 858B	E622          	INC sp
        * 858D	F003          	BEQ $8592
        * 858F	60            	RTS
        * 8590	E622          	INC sp
        * 8592	E623          	INC sp+1
        * 8594	60            	RTS
        */
        Write(M6502Instruction.INC_zpg, sp);
        Write(M6502Instruction.BEQ_rel, 0x05);
        Write(M6502Instruction.INC_zpg, sp);
        Write(M6502Instruction.BEQ_rel, 0x03);
        Write(M6502Instruction.RTS_impl);
        Write(M6502Instruction.INC_zpg, sp);
        Write(M6502Instruction.INC_zpg, sp + 1);
        Write(M6502Instruction.RTS_impl);
    }

    void Write_popa()
    {
        SetLabel(nameof(popa), (ushort)(_writer.BaseStream.Position + BaseAddress));
        /*
         * 8595	A000          	LDY #$00                      ; popa
         * 8597	B122          	LDA (sp),y
         * 8599	E622          	INC sp
         * 859B	F001          	BEQ $859E
         * 859D	60            	RTS
         * 859E	E623          	INC sp+1
         * 85A0	60            	RTS
         */
        Write(M6502Instruction.LDY, 0x00);
        Write(M6502Instruction.LDA_ind_Y, sp);
        Write(M6502Instruction.INC_zpg, sp);
        Write(M6502Instruction.BEQ_rel, 0x01);
        Write(M6502Instruction.RTS_impl);
        Write(M6502Instruction.INC_zpg, sp + 1);
        Write(M6502Instruction.RTS_impl);
    }

    void Write_pusha()
    {
        //
        /*
         * 85A1	A000          	LDY #$00                      ; pusha0sp
         * 85A3	B122          	LDA (sp),y                    ; pushaysp
         * 85A5	A422          	LDY sp                        ; pusha
         * 85A7	F007          	BEQ $85B0
         * 85A9	C622          	DEC sp
         * 85AB	A000          	LDY #$00
         * 85AD	9122          	STA (sp),y
         * 85AF	60            	RTS
         * 85B0	C623          	DEC sp+1
         * 85B2	C622          	DEC sp
         * 85B4	9122          	STA (sp),y
         * 85B6	60            	RTS
        */
        //SetLabel(nameof(pusha0sp), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(M6502Instruction.LDY, 0x00);

        //SetLabel(nameof(pushaysp), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(M6502Instruction.LDA_ind_Y, sp);

        SetLabel(nameof(pusha), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(M6502Instruction.LDY_zpg, sp);
        Write(M6502Instruction.BEQ_rel, PAL_UPDATE);
        Write(M6502Instruction.DEC_zpg, sp);
        Write(M6502Instruction.LDY, 0x00);
        Write(M6502Instruction.STA_ind_Y, sp);
        Write(M6502Instruction.RTS_impl);
        Write(M6502Instruction.DEC_zpg, sp + 1);
        Write(M6502Instruction.DEC_zpg, sp);
        Write(M6502Instruction.STA_ind_Y, sp);
        Write(M6502Instruction.RTS_impl);
    }

    void Write_pushax()
    {
        /*
        * 85B7	A900          	LDA #$00                      ; push0
        * 85B9	A200          	LDX #$00                      ; pusha0
        * 85BB	48            	PHA                           ; pushax
        * 85BC	A522          	LDA sp
        * 85BE	38            	SEC
        * 85BF	E902          	SBC #$02
        * 85C1	8522          	STA sp
        * 85C3	B002          	BCS $85C7
        * 85C5	C623          	DEC sp+1
        * 85C7	A001          	LDY #$01
        * 85C9	8A            	TXA
        * 85CA	9122          	STA (sp),y
        * 85CC	68            	PLA
        * 85CD	88            	DEY
        * 85CE	9122          	STA (sp),y
        * 85D0	60            	RTS
        */
        //SetLabel(nameof(push0), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(M6502Instruction.LDA, 0x00);

        //SetLabel(nameof(pusha0), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(M6502Instruction.LDX, 0x00);

        SetLabel(nameof(pushax), (ushort)(_writer.BaseStream.Position + BaseAddress));
        Write(M6502Instruction.PHA_impl);
        Write(M6502Instruction.LDA_zpg, sp);
        Write(M6502Instruction.SEC_impl);
        Write(M6502Instruction.SBC, 0x02);
        Write(M6502Instruction.STA_zpg, sp);
        Write(M6502Instruction.BCS, 0x02);
        Write(M6502Instruction.DEC_zpg, sp + 1);
        Write(M6502Instruction.LDY, 0x01);
        Write(M6502Instruction.TXA_impl);
        Write(M6502Instruction.STA_ind_Y, sp);
        Write(M6502Instruction.PLA_impl);
        Write(M6502Instruction.DEY_impl);
        Write(M6502Instruction.STA_ind_Y, sp);
        Write(M6502Instruction.RTS_impl);
    }

    void Write_zerobss(byte locals)
    {
        SetLabel(nameof(zerobss), (ushort)(_writer.BaseStream.Position + BaseAddress));
        /*
         * 85D1	A925          	LDA #$25                      ; zerobss
         * 85D3	852A          	STA ptr1
         * 85D5	A903          	LDA #$03
         * 85D7	852B          	STA ptr1+1
         * 85D9	A900          	LDA #$00
         * 85DB	A8            	TAY
         * 85DC	A200          	LDX #$00
         * 85DE	F00A          	BEQ $85EA
         * 85E0	912A          	STA (ptr1),y
         * 85E2	C8            	INY
         * 85E3	D0FB          	BNE $85E0
         * 85E5	E62B          	INC ptr1+1
         * 85E7	CA            	DEX
         * 85E8	D0F6          	BNE $85E0
         * 85EA	C000          	CPY #$00
         * 85EC	F005          	BEQ $85F3
         * 85EE	912A          	STA (ptr1),y
         * 85F0	C8            	INY
         * 85F1	D0F7          	BNE $85EA
         * 85F3	60            	RTS
         */
        Write(M6502Instruction.LDA, 0x25);
        Write(M6502Instruction.STA_zpg, ptr1);
        Write(M6502Instruction.LDA, 0x03);
        Write(M6502Instruction.STA_zpg, ptr1 + 1);
        Write(M6502Instruction.LDA, 0x00);
        Write(M6502Instruction.TAY_impl);
        Write(M6502Instruction.LDX, 0x00);
        Write(M6502Instruction.BEQ_rel, PAL_SPR_PTR);
        Write(M6502Instruction.STA_ind_Y, ptr1);
        Write(M6502Instruction.INY_impl);
        Write(M6502Instruction.BNE_rel, 0xFB);
        Write(M6502Instruction.INC_zpg, ptr1 + 1);
        Write(M6502Instruction.DEX_impl);
        Write(M6502Instruction.BNE_rel, 0xF6);
        Write(M6502Instruction.CPY, locals);
        Write(M6502Instruction.BEQ_rel, 0x05);
        Write(M6502Instruction.STA_ind_Y, ptr1);
        Write(M6502Instruction.INY_impl);
        Write(M6502Instruction.BNE_rel, 0xF7);
        Write(M6502Instruction.RTS_impl);
    }

    /// <summary>
    /// Writes an "implied" instruction that has no argument
    /// </summary>
    public void Write(M6502Instruction i)
    {
        LastLDA = i == M6502Instruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X})");
        _writer.Write((byte)i);
    }

    /// <summary>
    /// Writes an instruction with a single byte argument
    /// </summary>
    public void Write (M6502Instruction i, byte @byte)
    {
        LastLDA = i == M6502Instruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {@byte:X}");
        _writer.Write((byte)i);
        _writer.Write(@byte);
    }

    /// <summary>
    /// Writes an instruction with an address argument (2 bytes)
    /// </summary>
    public void Write(M6502Instruction i, ushort address)
    {
        LastLDA = i == M6502Instruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {address:X}");
        _writer.Write((byte)i);
        _writer.Write(address);
    }

    public void Write()
    {
        WriteHeader();
        if (PRG_ROM != null)
            _writer.Write(PRG_ROM);
        if (CHR_ROM != null)
            _writer.Write(CHR_ROM);
        if (Trainer != null)
            _writer.Write(Trainer);
        if (INST_ROM != null)
            _writer.Write(INST_ROM);
    }

    private void SetLabel(string name, ushort address)
    {
        if (_hasPresetLabels)
            return;
        Labels[name] = address;
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}

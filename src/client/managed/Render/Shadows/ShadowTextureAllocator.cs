//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

/// <summary>
/// clientshadowmgr.cpp's <c>CTextureAllocator</c>: the render-to-texture
/// shadows' 1024-square atlas, cut into sixteen 256 blocks of uniformly sized
/// fragments (one block of 16s, one of 32s, two of 64s, six of 128s and six of
/// 256s), each size an LRU list of fragments. A texture asks for the fragment
/// size its screen area wants (<see cref="UseTexture"/>); a size used up this
/// frame falls back to the next smaller.
///
/// <para>
/// The ORDER IS STOCK'S, to the fragment: the fragments are allocated as
/// <c>CUtlMultiList</c> hands out indices (from 0 after a purge), each block's
/// pushed onto its list's head in descending index (so the last block's
/// fragment 0 is the head), a use moves a fragment to the tail and a
/// deallocation to the head. Which fragment a texture gets is which rectangle
/// of the atlas it is drawn into, and SetShadowTexCoord says so.
/// </para>
/// </summary>
public sealed class ShadowTextureAllocator
{
    public const int Invalid = 0xFFFF;

    private const int TexturePageSize = 1024;
    private const int MaxTexturePower = 8;
    private const int MinTexturePower = 4;
    private const int MaxTextureSize = 1 << MaxTexturePower;
    private const int MinTextureSize = 1 << MinTexturePower;
    private const int BlockSize = MaxTextureSize;
    private const int BlocksPerRow = TexturePageSize / MaxTextureSize;
    private const int BlockCount = BlocksPerRow * BlocksPerRow;

    // The material system's names for the page, from imageformat.h,
    // imaterialsystem.h and texture_group_names.h.
    private const int RtSizeNoChange = 0;
    private const int ImageFormatArgb8888 = 11;
    private const int MaterialRtDepthNone = 2;

    private struct TextureInfo
    {
        public int Fragment;
        public int Size;
        public int Power;
    }

    private struct FragmentInfo
    {
        public int Block;
        public int Index;
        public int Texture;
        public uint FrameUsed;
        public int Prev;
        public int Next;
        public int List;   // the power whose LRU it is in, or -1
    }

    private readonly IShadowMaterials _materials;

    private readonly UtlLinkedListModel _textureHandles = new();
    private TextureInfo[] _textures = new TextureInfo[256];
    private FragmentInfo[] _fragments = new FragmentInfo[512];
    private int _fragmentCount;
    private readonly int[] _cacheHead = new int[MaxTexturePower + 1];
    private readonly int[] _cacheTail = new int[MaxTexturePower + 1];
    private readonly bool[] _cacheValid = new bool[MaxTexturePower + 1];
    private readonly int[] _blockPower = new int[BlockCount];
    private uint _currentFrame;

    public ShadowTextureAllocator(IShadowMaterials materials)
    {
        _materials = materials;
    }

    /// <summary>The page: the atlas render target's id (0 before Init and after Shutdown).</summary>
    public int Page { get; private set; }

    /// <summary><c>Init</c>: the lists invalid, and the 1024 page made.</summary>
    public void Init()
    {
        for (int i = 0; i <= MaxTexturePower; ++i)
            _cacheValid[i] = false;
        Page = _materials.InitRenderTarget("_rt_Shadows", TexturePageSize, TexturePageSize, RtSizeNoChange, ImageFormatArgb8888,
                                           MaterialRtDepthNone, false);
    }

    public void Shutdown()
    {
        _materials.ShutdownTexture(Page);
        Page = 0;
    }

    /// <summary><c>Reset</c>: everything dealloc'd, the blocks' sizes set, and each block's fragments put in its size's LRU.</summary>
    public void Reset()
    {
        DeallocateAllTextures();
        _blockPower[0] = MaxTexturePower - 4;
        _blockPower[1] = MaxTexturePower - 3;
        _blockPower[2] = MaxTexturePower - 2;
        _blockPower[3] = MaxTexturePower - 2;
        for (int b = 4; b <= 9; b++)
            _blockPower[b] = MaxTexturePower - 1;
        for (int b = 10; b <= 15; b++)
            _blockPower[b] = MaxTexturePower;
        for (int i = 0; i <= MaxTexturePower; ++i)
        {
            _cacheValid[i] = true;
            _cacheHead[i] = Invalid;
            _cacheTail[i] = Invalid;
        }
        for (int i = 0; i < BlockCount; ++i)
            AddBlockToLRU(i);
        _currentFrame = 0;
    }

    /// <summary><c>DeallocateAllTextures</c>: both lists purged, every cache list invalid.</summary>
    public void DeallocateAllTextures()
    {
        _textureHandles.Purge();
        _fragmentCount = 0;
        for (int i = 0; i <= MaxTexturePower; ++i)
            _cacheValid[i] = false;
    }

    private void AddBlockToLRU(int block)
    {
        int power = _blockPower[block];
        int size = 1 << power;
        int fragmentCount = MaxTextureSize / size;
        fragmentCount *= fragmentCount;
        while (--fragmentCount >= 0)
        {
            int f = _fragmentCount++;
            if (f >= _fragments.Length)
                Array.Resize(ref _fragments, _fragments.Length * 2);
            _fragments[f] = new FragmentInfo { Block = block, Index = fragmentCount, Texture = Invalid, FrameUsed = 0xFFFFFFFF, Prev = Invalid, Next = Invalid, List = -1 };
            LinkToHead(power, f);
        }
    }

    private void Unlink(int f)
    {
        ref FragmentInfo e = ref _fragments[f];
        if (e.List < 0)
            return;
        int list = e.List;
        if (e.Prev != Invalid)
            _fragments[e.Prev].Next = e.Next;
        else
            _cacheHead[list] = e.Next;
        if (e.Next != Invalid)
            _fragments[e.Next].Prev = e.Prev;
        else
            _cacheTail[list] = e.Prev;
        e.Prev = Invalid;
        e.Next = Invalid;
        e.List = -1;
    }

    private void LinkToHead(int list, int f)
    {
        Unlink(f);
        ref FragmentInfo e = ref _fragments[f];
        e.Prev = Invalid;
        e.Next = _cacheHead[list];
        if (_cacheHead[list] != Invalid)
            _fragments[_cacheHead[list]].Prev = f;
        else
            _cacheTail[list] = f;
        _cacheHead[list] = f;
        e.List = list;
    }

    private void LinkToTail(int list, int f)
    {
        Unlink(f);
        ref FragmentInfo e = ref _fragments[f];
        e.Next = Invalid;
        e.Prev = _cacheTail[list];
        if (_cacheTail[list] != Invalid)
            _fragments[_cacheTail[list]].Next = f;
        else
            _cacheHead[list] = f;
        _cacheTail[list] = f;
        e.List = list;
    }

    private void MarkUsed(int fragment)
    {
        int power = _blockPower[_fragments[fragment].Block];
        LinkToTail(power, fragment);
        _fragments[fragment].FrameUsed = _currentFrame;
    }

    private void MarkUnused(int fragment)
    {
        int power = _blockPower[_fragments[fragment].Block];
        LinkToHead(power, fragment);
    }

    /// <summary><c>AllocateTexture( w, h )</c>: clamped to 16..256, its power found.</summary>
    public int AllocateTexture(int w, int h)
    {
        if (w < MinTextureSize)
            w = MinTextureSize;
        else if (w > MaxTextureSize)
            w = MaxTextureSize;

        int handle = _textureHandles.AddToTail();
        if (handle >= _textures.Length)
            Array.Resize(ref _textures, Math.Max(_textures.Length * 2, handle + 1));
        int power = 0, size = 1;
        while (size < w)
        {
            size <<= 1;
            ++power;
        }
        _textures[handle] = new TextureInfo { Fragment = Invalid, Size = w, Power = power };
        return handle;
    }

    public void DeallocateTexture(int h)
    {
        if (_textures[h].Fragment != Invalid)
        {
            MarkUnused(_textures[h].Fragment);
            _fragments[_textures[h].Fragment].FrameUsed = 0xFFFFFFFF;
            DisconnectTextureFromFragment(_textures[h].Fragment);
        }
        _textureHandles.Remove(h);
    }

    private void DisconnectTextureFromFragment(int f)
    {
        ref FragmentInfo info = ref _fragments[f];
        if (info.Texture != Invalid)
        {
            _textures[info.Texture].Fragment = Invalid;
            info.Texture = Invalid;
        }
    }

    public bool HasValidTexture(int h) => _textures[h].Fragment != Invalid;

    private int FragmentPower(int f) => _blockPower[_fragments[f].Block];

    private int Head(int power) => _cacheValid[power] ? _cacheHead[power] : Invalid;

    /// <summary>
    /// <c>UseTexture( h, willRedraw, area )</c>: true when the texture needs
    /// redrawing (it moved to a fragment). The size its area wants, capped at
    /// its own; a fragment of that size or within one smaller is kept when it
    /// will not be redrawn anyway; else the LRU head of the wanted size, or of
    /// the next smaller when that size is used up this frame, if it is bigger
    /// than what it has.
    /// </summary>
    public bool UseTexture(int h, bool willRedraw, float area)
    {
        ref TextureInfo info = ref _textures[h];

        int desiredPower = MinTexturePower;
        int desiredWidth = MinTextureSize;
        while ((desiredWidth * desiredWidth) < area)
        {
            if (desiredPower >= info.Power)
            {
                desiredPower = info.Power;
                break;
            }
            ++desiredPower;
            desiredWidth <<= 1;
        }

        int currentPower = -1;
        int currentFragment = info.Fragment;
        if (currentFragment != Invalid)
        {
            currentPower = FragmentPower(info.Fragment);
            bool shouldKeepTexture = !willRedraw && desiredPower < 8 && desiredPower - currentPower <= 1;
            if (currentPower == desiredPower || shouldKeepTexture)
            {
                MarkUsed(currentFragment);
                return false;
            }
        }

        int power = desiredPower;
        int f = Invalid;
        bool done = false;
        while (!done && power >= 0)
        {
            f = Head(power);
            if (f != Invalid && _fragments[f].FrameUsed != _currentFrame)
                done = true;
            else
                --power;
        }

        if (currentFragment != Invalid)
        {
            if (power <= currentPower)
            {
                MarkUsed(currentFragment);
                return false;
            }
            DisconnectTextureFromFragment(currentFragment);
        }

        if (f == Invalid)
            return false;

        DisconnectTextureFromFragment(f);
        info.Fragment = f;
        _fragments[f].Texture = h;
        MarkUsed(f);
        return true;
    }

    public void AdvanceFrame() => _currentFrame++;

    /// <summary><c>GetTextureRect( handle, x, y, w, h )</c>.</summary>
    public void GetTextureRect(int handle, out int x, out int y, out int w, out int h)
    {
        ref FragmentInfo fragment = ref _fragments[_textures[handle].Fragment];
        int blockY = fragment.Block / BlocksPerRow;
        int blockX = fragment.Block - blockY * BlocksPerRow;
        int fragmentSize = 1 << _blockPower[fragment.Block];
        int fragmentsPerRow = BlockSize / fragmentSize;
        int fragmentY = fragment.Index / fragmentsPerRow;
        int fragmentX = fragment.Index - fragmentY * fragmentsPerRow;
        x = blockX * BlockSize + fragmentX * fragmentSize;
        y = blockY * BlockSize + fragmentY * fragmentSize;
        w = fragmentSize;
        h = fragmentSize;
    }

    public static int TotalTextureSize => TexturePageSize;

    /// <summary>A texture's fragment power, or -1 without one (the facts' view).</summary>
    public int CurrentPower(int h) => _textures[h].Fragment == Invalid ? -1 : FragmentPower(_textures[h].Fragment);

    public void Save(ReloadWriter writer)
    {
        writer.Write(Page);
        writer.Write(unchecked((int)_currentFrame));
        _textureHandles.Save(writer);
        for (int i = 0; i <= _textureHandles.LastAlloc; i++)
        {
            writer.Write(_textures[i].Fragment);
            writer.Write(_textures[i].Size);
            writer.Write(_textures[i].Power);
        }
        writer.Write(_fragmentCount);
        for (int i = 0; i < _fragmentCount; i++)
        {
            ref FragmentInfo f = ref _fragments[i];
            writer.Write(f.Block);
            writer.Write(f.Index);
            writer.Write(f.Texture);
            writer.Write(unchecked((int)f.FrameUsed));
            writer.Write(f.Prev);
            writer.Write(f.Next);
            writer.Write(f.List);
        }
        for (int i = 0; i <= MaxTexturePower; i++)
        {
            writer.Write(_cacheHead[i]);
            writer.Write(_cacheTail[i]);
            writer.Write(_cacheValid[i]);
        }
        for (int i = 0; i < BlockCount; i++)
            writer.Write(_blockPower[i]);
    }

    public void Restore(ReloadReader reader)
    {
        Page = reader.ReadInt();
        _currentFrame = unchecked((uint)reader.ReadInt());
        _textureHandles.Restore(reader);
        _textures = new TextureInfo[Math.Max(256, _textureHandles.LastAlloc + 1)];
        for (int i = 0; i <= _textureHandles.LastAlloc; i++)
            _textures[i] = new TextureInfo { Fragment = reader.ReadInt(), Size = reader.ReadInt(), Power = reader.ReadInt() };
        _fragmentCount = reader.ReadInt();
        _fragments = new FragmentInfo[Math.Max(512, _fragmentCount)];
        for (int i = 0; i < _fragmentCount; i++)
        {
            _fragments[i] = new FragmentInfo
            {
                Block = reader.ReadInt(),
                Index = reader.ReadInt(),
                Texture = reader.ReadInt(),
                FrameUsed = unchecked((uint)reader.ReadInt()),
                Prev = reader.ReadInt(),
                Next = reader.ReadInt(),
                List = reader.ReadInt(),
            };
        }
        for (int i = 0; i <= MaxTexturePower; i++)
        {
            _cacheHead[i] = reader.ReadInt();
            _cacheTail[i] = reader.ReadInt();
            _cacheValid[i] = reader.ReadBool();
        }
        for (int i = 0; i < BlockCount; i++)
            _blockPower[i] = reader.ReadInt();
    }
}

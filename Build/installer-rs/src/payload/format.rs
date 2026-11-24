//! The payload's on-disk shape, shared by the setup exe (reader) and `pack-payload` (writer).
//!
//!   [ setup exe bytes ][ xz( tar ) ][ footer ]
//!
//! The tar holds the install folder as-is: the core files (add-in build output, which
//! includes the Kitvei Hakodesh app and its service) at the root, the typing tutor
//! under `TypingTutor\`. The footer sits in the last `FOOTER_LEN` bytes of the exe.

pub const MAGIC: &[u8; 8] = b"KKPAYLD2";

/// Independent xz blocks of 16 MB uncompressed; the dictionary matches (a larger one
/// can't reach past its block). The setup needs about this much RAM per block it unpacks.
#[allow(dead_code)] // used by pack-payload
pub const BLOCK_SIZE: u64 = 16 << 20;
#[allow(dead_code)]
pub const DICT_SIZE: u32 = BLOCK_SIZE as u32;
/// liblzma's "extreme" preset flag (LZMA_PRESET_EXTREME).
#[allow(dead_code)]
pub const PRESET_EXTREME: u32 = 0x8000_0000;
pub const FOOTER_LEN: usize = 32;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Footer {
    /// Where the compressed tar starts (= the length of the bare setup exe).
    pub offset: u64,
    /// Total uncompressed size of all files, for the progress bar.
    pub total_bytes: u64,
    pub file_count: u64,
}

impl Footer {
    #[allow(dead_code)] // used by pack-payload
    pub fn to_bytes(self) -> [u8; FOOTER_LEN] {
        let mut out = [0u8; FOOTER_LEN];
        out[0..8].copy_from_slice(&self.offset.to_le_bytes());
        out[8..16].copy_from_slice(&self.total_bytes.to_le_bytes());
        out[16..24].copy_from_slice(&self.file_count.to_le_bytes());
        out[24..32].copy_from_slice(MAGIC);
        out
    }

    #[allow(dead_code)] // used by the setup exe
    pub fn from_bytes(bytes: &[u8]) -> Option<Self> {
        if bytes.len() != FOOTER_LEN || &bytes[24..32] != MAGIC {
            return None;
        }
        let u64_at = |at: usize| u64::from_le_bytes(bytes[at..at + 8].try_into().unwrap());
        Some(Self { offset: u64_at(0), total_bytes: u64_at(8), file_count: u64_at(16) })
    }
}

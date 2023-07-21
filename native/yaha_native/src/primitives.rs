use hyper::Version;

#[repr(i32)]
#[derive(Debug)]
pub enum YahaHttpVersion {
    Http09,
    Http10,
    Http11,
    Http2,
    Http3,
}

impl From<Version> for YahaHttpVersion {
    fn from(value: Version) -> Self {
        match value {
            Version::HTTP_09 => YahaHttpVersion::Http09,
            Version::HTTP_10 => YahaHttpVersion::Http10,
            Version::HTTP_11 => YahaHttpVersion::Http11,
            Version::HTTP_2 => YahaHttpVersion::Http2,
            Version::HTTP_3 => YahaHttpVersion::Http3,
            _ => panic!("Unsupported Version"),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum MatchPath {
    Ascii,
    Utf16,
}

pub struct Matcher {
    query_ascii_lower: Vec<u8>,
    query_utf16_lower: Vec<u16>,
    path: MatchPath,
}

impl Matcher {
    pub fn new(query: impl Into<String>) -> Self {
        let normalized = query.into().trim().to_string();
        if normalized.is_ascii() {
            let query_ascii_lower = normalized
                .as_bytes()
                .iter()
                .map(|b| b.to_ascii_lowercase())
                .collect();
            return Self {
                query_ascii_lower,
                query_utf16_lower: Vec::new(),
                path: MatchPath::Ascii,
            };
        }

        let lowered = normalized.to_lowercase();
        let query_utf16_lower = lowered.encode_utf16().collect();
        Self {
            query_ascii_lower: Vec::new(),
            query_utf16_lower,
            path: MatchPath::Utf16,
        }
    }

    pub fn is_empty(&self) -> bool {
        match self.path {
            MatchPath::Ascii => self.query_ascii_lower.is_empty(),
            MatchPath::Utf16 => self.query_utf16_lower.is_empty(),
        }
    }

    pub fn is_match_ascii(&self, haystack: &str) -> bool {
        if self.path != MatchPath::Ascii {
            return self.is_match(haystack);
        }
        if self.query_ascii_lower.is_empty() {
            return true;
        }
        contains_ascii_case_insensitive(haystack.as_bytes(), &self.query_ascii_lower)
    }

    pub fn is_match(&self, haystack: &str) -> bool {
        match self.path {
            MatchPath::Ascii => self.is_match_ascii(haystack),
            MatchPath::Utf16 => {
                if self.query_utf16_lower.is_empty() {
                    return true;
                }
                let lowered = haystack.to_lowercase();
                let hay_utf16: Vec<u16> = lowered.encode_utf16().collect();
                contains_u16_slice(&hay_utf16, &self.query_utf16_lower)
            }
        }
    }
}

fn contains_ascii_case_insensitive(haystack: &[u8], needle_lower: &[u8]) -> bool {
    if needle_lower.is_empty() {
        return true;
    }
    if haystack.len() < needle_lower.len() {
        return false;
    }

    // Prefix filter: reject quickly if first lowercase byte never appears.
    let first = needle_lower[0];
    if !haystack
        .iter()
        .map(|b| b.to_ascii_lowercase())
        .any(|b| b == first)
    {
        return false;
    }

    haystack.windows(needle_lower.len()).any(|w| {
        w.iter()
            .zip(needle_lower.iter())
            .all(|(a, b)| a.to_ascii_lowercase() == *b)
    })
}

fn contains_u16_slice(haystack: &[u16], needle: &[u16]) -> bool {
    if needle.is_empty() {
        return true;
    }
    if haystack.len() < needle.len() {
        return false;
    }

    // Prefix filter for UTF-16 path.
    let first = needle[0];
    if !haystack.iter().any(|u| *u == first) {
        return false;
    }

    haystack.windows(needle.len()).any(|w| w == needle)
}

#[cfg(test)]
mod tests {
    use super::Matcher;

    #[test]
    fn ascii_path_matches_case_insensitive() {
        let m = Matcher::new("AbC");
        assert!(m.is_match("xxabcxx"));
        assert!(m.is_match("xxAbCxx"));
        assert!(!m.is_match("xxacxx"));
    }

    #[test]
    fn utf16_path_matches_unicode() {
        let m = Matcher::new("文档");
        assert!(m.is_match("我的文档目录"));
        assert!(!m.is_match("我的图片目录"));
    }
}

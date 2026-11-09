//! Safely manages native Chrome PiP and Discord pop-out video windows.
use serde::{Deserialize, Serialize};
use std::{
    collections::{HashMap, HashSet},
    ffi::c_void,
    fs, io,
    path::PathBuf,
    ptr::null_mut,
};
use windows_sys::Win32::{
    Foundation::{CloseHandle, FILETIME, GetLastError, HWND, RECT, SetLastError},
    Graphics::Dwm::DwmGetWindowAttribute,
    Storage::FileSystem::FileTimeToLocalFileTime,
    System::Threading::{
        GetProcessTimes, OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION, QueryFullProcessImageNameW,
    },
    UI::WindowsAndMessaging::{
        EnumWindows, GWL_EXSTYLE, GetClassNameW, GetLayeredWindowAttributes, GetSystemMetrics,
        GetWindowLongPtrW, GetWindowPlacement, GetWindowRect, GetWindowTextW,
        GetWindowThreadProcessId, HWND_NOTOPMOST, HWND_TOPMOST, IsIconic, IsWindow,
        IsWindowVisible, IsZoomed, LWA_ALPHA, SM_CXSCREEN, SM_CYSCREEN, SWP_FRAMECHANGED,
        SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE, SWP_NOZORDER, SetLayeredWindowAttributes,
        SetWindowLongPtrW, SetWindowPos, WINDOWPLACEMENT, WS_EX_LAYERED, WS_EX_NOACTIVATE,
        WS_EX_TOPMOST, WS_EX_TRANSPARENT,
    },
};

const CONTROLLED: u32 = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
const BROWSERS: &[&str] = &["chrome", "msedge", "brave", "chromium", "vivaldi", "opera"];
const DISCORD_CLIENTS: &[&str] = &["discord", "discordptb", "discordcanary"];

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum WindowKind {
    BrowserPip,
    DiscordStream,
    DiscordPopout,
    BrowserPopup,
}

pub fn is_discord_process(name: &str) -> bool {
    DISCORD_CLIENTS.contains(&name)
}

/// Discord titles observed on actual Windows pop-out streams (including
/// Vietnamese "Stream của username"). Never match a chat/channel title or
/// the generic word "stream" elsewhere in the title.
pub fn is_discord_stream_title(title: &str) -> bool {
    let title = title.trim();
    let lower = title.to_lowercase();
    let remainder = lower
        .strip_prefix("stream của ")
        .or_else(|| lower.strip_prefix("stream of "))
        .or_else(|| lower.strip_prefix("stream from "))
        .or_else(|| lower.strip_suffix("'s stream"))
        .or_else(|| lower.strip_suffix("’s stream"));
    remainder.is_some_and(|name| !name.trim().is_empty())
}

/// Main Discord windows identify themselves with a Discord suffix.
/// Pop-out video windows may use just the current voice-channel name instead.
pub fn is_discord_main_title(title: &str) -> bool {
    let title = title.trim().to_lowercase();
    title == "discord" || title.ends_with(" - discord")
}

pub fn is_discord_popout_candidate(title: &str) -> bool {
    let t = title.trim().to_lowercase();
    !t.is_empty()
        && !is_discord_main_title(&t)
        && t != "discord overlay"
        && t != "discord overlay input trap"
        && t != "discord updater"
        && t != "discord update"
        && t != "dde server window"
}

fn verified_discord_popout(
    main_window_present: bool,
    minimized: bool,
    maximized: bool,
    width: i32,
    height: i32,
    screen_width: i32,
    screen_height: i32,
) -> bool {
    main_window_present
        && !minimized
        && !maximized
        && !(screen_width > 0
            && screen_height > 0
            && width >= screen_width * 95 / 100
            && height >= screen_height * 95 / 100)
}

pub fn classify_window(
    process: &str,
    class: &str,
    title: &str,
    pinned: bool,
) -> Option<(WindowKind, bool)> {
    if !class.starts_with("Chrome_WidgetWin_") {
        return None;
    }
    if BROWSERS.contains(&process) {
        if !pinned {
            return None;
        }
        return Some((
            if is_pip_title(title) {
                WindowKind::BrowserPip
            } else {
                WindowKind::BrowserPopup
            },
            is_pip_title(title),
        ));
    }
    if is_discord_process(process) && is_discord_stream_title(title) {
        // Discord permits pop-out windows to be unpinned. Show those in the
        // picker but do not automatically intercept mouse input until pinned.
        return Some((WindowKind::DiscordStream, pinned));
    }
    None
}

fn utf16(buf: &[u16]) -> String {
    String::from_utf16_lossy(&buf[..buf.iter().position(|&n| n == 0).unwrap_or(buf.len())])
}

pub fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}
fn winerr() -> io::Error {
    io::Error::last_os_error()
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PipWindow {
    pub handle: i64,
    pub pid: u32,
    pub process_name: String,
    pub title: String,
    pub window_class: String,
    pub width: i32,
    pub height: i32,
    pub topmost: bool,
    pub minimized: bool,
    pub strict_match: bool,
    pub kind: WindowKind,
}
impl PipWindow {
    pub fn label(&self) -> String {
        format!(
            "{}{} | {} | {}x{} | 0x{:X}",
            match self.kind {
                WindowKind::DiscordStream if self.strict_match => "[Discord stream] ",
                WindowKind::DiscordStream => "[Discord stream / unpinned] ",
                WindowKind::DiscordPopout if self.strict_match => "[Discord pop-out] ",
                WindowKind::DiscordPopout => "[Discord pop-out / minimized] ",
                WindowKind::BrowserPip => "[PiP] ",
                WindowKind::BrowserPopup => "[Manual] ",
            },
            browser(&self.process_name),
            if self.title.is_empty() {
                "(no title)"
            } else {
                &self.title
            },
            self.width,
            self.height,
            self.handle
        )
    }
}
pub fn browser(s: &str) -> &'static str {
    match s {
        "chrome" => "Chrome",
        "msedge" => "Edge",
        "brave" => "Brave",
        "opera" => "Opera",
        "vivaldi" => "Vivaldi",
        "chromium" => "Chromium",
        "discord" => "Discord",
        "discordptb" => "Discord PTB",
        "discordcanary" => "Discord Canary",
        _ => "Browser",
    }
}
pub fn is_pip_title(title: &str) -> bool {
    matches!(
        title.trim().to_lowercase().as_str(),
        "picture in picture"
            | "picture-in-picture"
            | "picture in picture - google chrome"
            | "hình trong hình"
            | "ảnh trong ảnh"
            | "ピクチャー イン ピクチャー"
            | "ピクチャー イン ピクチャ"
            | "화면 속 화면"
    )
}
fn class(hwnd: HWND) -> String {
    let mut b = [0u16; 256];
    unsafe {
        GetClassNameW(hwnd, b.as_mut_ptr(), b.len() as i32);
    }
    utf16(&b)
}
fn title(hwnd: HWND) -> String {
    let mut b = [0u16; 512];
    unsafe {
        GetWindowTextW(hwnd, b.as_mut_ptr(), b.len() as i32);
    }
    utf16(&b)
}
pub fn exstyle(hwnd: HWND) -> u32 {
    unsafe { GetWindowLongPtrW(hwnd, GWL_EXSTYLE) as u32 }
}
fn topmost(hwnd: HWND) -> bool {
    exstyle(hwnd) & WS_EX_TOPMOST != 0
}
fn pid(hwnd: HWND) -> u32 {
    let mut id = 0;
    unsafe {
        GetWindowThreadProcessId(hwnd, &mut id);
    }
    id
}
fn process_info(id: u32) -> Option<(String, i64)> {
    unsafe {
        let h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, id);
        if h.is_null() {
            return None;
        }
        let mut b = [0u16; 2048];
        let mut len = b.len() as u32;
        let path_ok = QueryFullProcessImageNameW(h, 0, b.as_mut_ptr(), &mut len) != 0;
        let mut created = FILETIME {
            dwLowDateTime: 0,
            dwHighDateTime: 0,
        };
        let mut exit = created;
        let mut kernel = created;
        let mut user = created;
        let times_ok = GetProcessTimes(h, &mut created, &mut exit, &mut kernel, &mut user) != 0;
        CloseHandle(h);
        if !path_ok || !times_ok {
            return None;
        }
        let path = String::from_utf16_lossy(&b[..len as usize]);
        let name = path
            .rsplit(['\\', '/'])
            .next()
            .unwrap_or("")
            .trim_end_matches(".exe")
            .to_lowercase();
        // Match .NET Process.StartTime.Ticks from the original C# recovery journal
        // (local DateTime ticks since year 1, not UTC FILETIME ticks since 1601).
        let mut local = created;
        if FileTimeToLocalFileTime(&created, &mut local) == 0 {
            return None;
        }
        let ticks = (((local.dwHighDateTime as u64) << 32 | local.dwLowDateTime as u64)
            + 504_911_232_000_000_000) as i64;
        Some((name, ticks))
    }
}
pub fn scan() -> Vec<PipWindow> {
    struct Scan {
        windows: Vec<PipWindow>,
        cache: HashMap<u32, Option<String>>,
        discord_main_pids: HashSet<u32>,
        own_pid: u32,
    }
    unsafe extern "system" fn visit(hwnd: HWND, context: isize) -> i32 {
        let s = unsafe { &mut *(context as *mut Scan) };
        if unsafe { IsWindowVisible(hwnd) } == 0 {
            return 1;
        }
        let id = pid(hwnd);
        if id == 0 || id == s.own_pid {
            return 1;
        }
        let name = s
            .cache
            .entry(id)
            .or_insert_with(|| process_info(id).map(|v| v.0));
        if !name
            .as_ref()
            .is_some_and(|n| BROWSERS.contains(&n.as_str()) || is_discord_process(n))
        {
            return 1;
        }
        let cls = class(hwnd);
        if !cls.starts_with("Chrome_WidgetWin_") {
            return 1;
        }
        let minimized = unsafe { IsIconic(hwnd) != 0 };
        let mut rect = RECT::default();
        if minimized {
            let mut placement = WINDOWPLACEMENT {
                length: std::mem::size_of::<WINDOWPLACEMENT>() as u32,
                ..Default::default()
            };
            if unsafe { GetWindowPlacement(hwnd, &mut placement) } == 0 {
                return 1;
            }
            rect = placement.rcNormalPosition;
        } else if unsafe { GetWindowRect(hwnd, &mut rect) } == 0 {
            return 1;
        }
        let width = rect.right - rect.left;
        let height = rect.bottom - rect.top;
        if width < 120 || height < 80 {
            return 1;
        }
        let mut cloaked: u32 = 0;
        if unsafe { DwmGetWindowAttribute(hwnd, 14, &mut cloaked as *mut _ as *mut c_void, 4) } == 0
            && cloaked != 0
        {
            return 1;
        }
        let text = title(hwnd);
        let pinned = topmost(hwnd);
        let process = name.as_deref().unwrap_or("");
        if is_discord_process(process) && is_discord_main_title(&text) {
            s.discord_main_pids.insert(id);
            return 1;
        }
        let classification = classify_window(process, &cls, &text, pinned).or_else(|| {
            (is_discord_process(process) && is_discord_popout_candidate(&text))
                .then_some((WindowKind::DiscordPopout, false))
        });
        let Some((kind, mut strict_match)) = classification else {
            return 1;
        };
        // A minimized Electron window's HWND is still 'visible' but cannot
        // receive normal mouse input. Show it in the picker; do not auto-capture.
        if minimized {
            strict_match = false;
        }
        s.windows.push(PipWindow {
            handle: hwnd as isize as i64,
            pid: id,
            process_name: name.clone().unwrap_or_default(),
            title: text.clone(),
            window_class: cls,
            width,
            height,
            topmost: pinned,
            minimized,
            strict_match,
            kind,
        });
        1
    }
    let mut state = Scan {
        windows: Vec::new(),
        cache: HashMap::new(),
        discord_main_pids: HashSet::new(),
        own_pid: std::process::id(),
    };
    unsafe {
        EnumWindows(Some(visit), &mut state as *mut _ as isize);
    }
    let screen_width = unsafe { GetSystemMetrics(SM_CXSCREEN) };
    let screen_height = unsafe { GetSystemMetrics(SM_CYSCREEN) };
    state.windows.retain_mut(|w| {
        if w.kind != WindowKind::DiscordPopout {
            return true;
        }
        // Do not mistake a Discord main window, game overlay or browser tab
        // for a popped-out stream. The standalone window must coexist with
        // its normal Discord main window in the same OS process.
        if !state.discord_main_pids.contains(&w.pid) {
            return false;
        }
        let hwnd = w.handle as isize as HWND;
        w.strict_match = verified_discord_popout(
            true,
            w.minimized,
            unsafe { IsZoomed(hwnd) != 0 },
            w.width,
            w.height,
            screen_width,
            screen_height,
        );
        true
    });
    state.windows.sort_by(|a, b| {
        b.strict_match
            .cmp(&a.strict_match)
            .then(a.process_name.cmp(&b.process_name))
    });
    state.windows
}
pub fn same_window(w: &PipWindow) -> bool {
    let hwnd = w.handle as isize as HWND;
    (unsafe { IsWindow(hwnd) != 0 }) && pid(hwnd) == w.pid && class(hwnd) == w.window_class
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct RecoveryItem {
    handle: i64,
    pid: u32,
    process_start_ticks: i64,
    window_class: String,
    original_ex_style: i64,
    original_topmost: bool,
    had_layer_attributes: bool,
    original_color_key: u32,
    original_alpha: u8,
    original_layer_flags: u32,
}
impl RecoveryItem {
    fn same(&self) -> bool {
        let hwnd = self.handle as isize as HWND;
        // Compatible with the C# journal. Still fail-closed if PID was recycled
        // or a different window now occupies this HWND.
        (unsafe { IsWindow(hwnd) != 0 })
            && pid(hwnd) == self.pid
            && class(hwnd) == self.window_class
            && process_info(self.pid).is_some_and(|(_, t)| t == self.process_start_ticks)
    }
    fn snapshot(w: &PipWindow) -> io::Result<Self> {
        if !same_window(w) {
            return Err(io::Error::other("Window identity changed"));
        }
        let (_, started) =
            process_info(w.pid).ok_or_else(|| io::Error::other("Browser process unavailable"))?;
        let hwnd = w.handle as isize as HWND;
        let style = exstyle(hwnd);
        let mut color = 0;
        let mut alpha = 255;
        let mut flags = 0;
        let had = style & WS_EX_LAYERED != 0
            && unsafe { GetLayeredWindowAttributes(hwnd, &mut color, &mut alpha, &mut flags) != 0 };
        Ok(Self {
            handle: w.handle,
            pid: w.pid,
            process_start_ticks: started,
            window_class: w.window_class.clone(),
            original_ex_style: style as i64,
            original_topmost: topmost(hwnd),
            had_layer_attributes: had,
            original_color_key: color,
            original_alpha: alpha,
            original_layer_flags: flags,
        })
    }
}
fn write_style(hwnd: HWND, style: u32) -> io::Result<()> {
    unsafe {
        SetLastError(0);
        let previous = SetWindowLongPtrW(hwnd, GWL_EXSTYLE, style as isize);
        if previous == 0 && GetLastError() != 0 {
            return Err(winerr());
        }
        if SetWindowPos(
            hwnd,
            null_mut(),
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED,
        ) == 0
        {
            return Err(winerr());
        }
    }
    Ok(())
}
fn set_topmost(hwnd: HWND, value: bool) -> io::Result<()> {
    let after = if value { HWND_TOPMOST } else { HWND_NOTOPMOST };
    if unsafe {
        SetWindowPos(
            hwnd,
            after,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
        )
    } == 0
    {
        return Err(winerr());
    }
    Ok(())
}
fn restore(s: &RecoveryItem) -> io::Result<()> {
    if !s.same() {
        return Ok(());
    }
    let hwnd = s.handle as isize as HWND;
    let before = exstyle(hwnd);
    let after = (before & !CONTROLLED) | (s.original_ex_style as u32 & CONTROLLED);
    if s.original_ex_style as u32 & WS_EX_LAYERED != 0 && s.had_layer_attributes {
        if unsafe {
            SetLayeredWindowAttributes(
                hwnd,
                s.original_color_key,
                s.original_alpha,
                s.original_layer_flags,
            )
        } == 0
        {
            return Err(winerr());
        }
    }
    if before != after {
        write_style(hwnd, after)?;
    }
    if topmost(hwnd) != s.original_topmost {
        set_topmost(hwnd, s.original_topmost)?;
    }
    Ok(())
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct Settings {
    pub auto_detect: bool,
    pub auto_enable: bool,
    pub auto_discord_streams: bool,
    pub always_on_top: bool,
    pub opacity: i32,
    pub minimize_to_tray: bool,
}
impl Default for Settings {
    fn default() -> Self {
        Self {
            auto_detect: true,
            auto_enable: true,
            auto_discord_streams: true,
            always_on_top: true,
            opacity: 100,
            minimize_to_tray: true,
        }
    }
}
pub fn data_dir() -> PathBuf {
    let base = std::env::var_os("LOCALAPPDATA").unwrap_or_else(|| ".".into());
    PathBuf::from(base).join("PiPClickThrough")
}
impl Settings {
    pub fn load() -> Self {
        fs::read(data_dir().join("settings.json"))
            .ok()
            .and_then(|b| serde_json::from_slice::<Self>(&b).ok())
            .unwrap_or_default()
    }
    pub fn save(&self) -> io::Result<()> {
        fs::create_dir_all(data_dir())?;
        fs::write(
            data_dir().join("settings.json"),
            serde_json::to_vec_pretty(self)?,
        )
    }
}

pub struct Engine {
    tracked: HashMap<i64, RecoveryItem>,
    journal: PathBuf,
    pub click_through: bool,
    pub always_on_top: bool,
    pub opacity: i32,
    pub last_error: String,
}
impl Engine {
    pub fn new(settings: &Settings) -> io::Result<Self> {
        fs::create_dir_all(data_dir())?;
        let mut e = Self {
            tracked: HashMap::new(),
            journal: data_dir().join("recovery.json"),
            click_through: settings.auto_enable,
            always_on_top: settings.always_on_top,
            opacity: settings.opacity.clamp(20, 100),
            last_error: String::new(),
        };
        e.recover();
        Ok(e)
    }
    pub fn handles(&self) -> Vec<i64> {
        self.tracked.keys().copied().collect()
    }
    pub fn count(&self) -> usize {
        self.tracked.len()
    }
    fn persist(&self) -> io::Result<()> {
        let path = self.journal.with_extension("json.tmp");
        let states: Vec<_> = self.tracked.values().collect();
        fs::write(&path, serde_json::to_vec_pretty(&states)?)?;
        fs::rename(path, &self.journal)
    }
    fn recover(&mut self) {
        if let Ok(bytes) = fs::read(&self.journal) {
            match serde_json::from_slice::<Vec<RecoveryItem>>(&bytes) {
                Ok(states) => {
                    for state in states {
                        if state.same() && restore(&state).is_err() {
                            self.tracked.insert(state.handle, state);
                        }
                    }
                    if let Err(err) = self.persist() {
                        self.last_error = format!("Recovery journal: {err}");
                    }
                }
                Err(err) => {
                    self.last_error = format!("Invalid recovery journal retained: {err}");
                }
            }
        }
    }
    fn apply(&self, state: &RecoveryItem) -> io::Result<()> {
        if !state.same() {
            return Ok(());
        }
        let hwnd = state.handle as isize as HWND;
        let before = exstyle(hwnd);
        let mut after = before;
        if self.click_through {
            after |= CONTROLLED;
        } else {
            after = (after & !CONTROLLED) | (state.original_ex_style as u32 & CONTROLLED);
            if self.opacity < 100 {
                after |= WS_EX_LAYERED;
            }
        }
        if before != after {
            write_style(hwnd, after)?;
        }
        if after & WS_EX_LAYERED != 0 {
            let alpha = (self.opacity.clamp(20, 100) * 255 / 100) as u8;
            if unsafe { SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA) } == 0 {
                return Err(winerr());
            }
        }
        if topmost(hwnd) != self.always_on_top {
            set_topmost(hwnd, self.always_on_top)?;
        }
        Ok(())
    }
    pub fn capture(&mut self, w: &PipWindow) -> io::Result<()> {
        if self.tracked.contains_key(&w.handle) {
            return Ok(());
        }
        let state = RecoveryItem::snapshot(w)?;
        self.tracked.insert(w.handle, state.clone());
        // Fail closed: do NOT change any style unless durable recovery succeeded.
        if let Err(err) = self.persist() {
            self.tracked.remove(&w.handle);
            return Err(err);
        }
        if let Err(err) = self.apply(&state) {
            let _ = restore(&state);
            self.tracked.remove(&w.handle);
            let _ = self.persist();
            return Err(err);
        }
        Ok(())
    }
    pub fn enforce(&mut self) {
        let dead: HashSet<i64> = self
            .tracked
            .iter()
            .filter(|(_, state)| !state.same())
            .map(|(h, _)| *h)
            .collect();
        if !dead.is_empty() {
            self.tracked.retain(|h, _| !dead.contains(h));
            if let Err(e) = self.persist() {
                self.last_error = e.to_string();
            }
        }
        for state in self.tracked.values() {
            if let Err(e) = self.apply(state) {
                self.last_error = e.to_string();
            }
        }
    }
    pub fn set_click_through(&mut self, value: bool) {
        self.click_through = value;
        self.enforce();
    }
    pub fn set_top(&mut self, value: bool) {
        self.always_on_top = value;
        self.enforce();
    }
    pub fn set_opacity(&mut self, value: i32) {
        self.opacity = value.clamp(20, 100);
        self.enforce();
    }
    pub fn restore_all(&mut self) -> io::Result<()> {
        let mut failed = HashMap::new();
        let mut first = None;
        for (h, s) in &self.tracked {
            if let Err(e) = restore(s) {
                if first.is_none() {
                    first = Some(e);
                }
                failed.insert(*h, s.clone());
            }
        }
        self.tracked = failed;
        self.persist()?;
        if let Some(e) = first {
            return Err(e);
        }
        Ok(())
    }
}
impl Drop for Engine {
    fn drop(&mut self) {
        let _ = self.restore_all();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn strict_titles_are_exact() {
        assert!(is_pip_title("Picture in Picture"));
        assert!(is_pip_title("Hình trong hình"));
        assert!(!is_pip_title("Picture in Picture - arbitrary web page"));
    }
    #[test]
    fn discord_popout_classification_never_matches_main_chat() {
        for process in ["discord", "discordptb", "discordcanary"] {
            assert_eq!(
                classify_window(process, "Chrome_WidgetWin_1", "Stream của thwhnim", true),
                Some((WindowKind::DiscordStream, true))
            );
            assert_eq!(
                classify_window(process, "Chrome_WidgetWin_1", "Stream of User", false),
                Some((WindowKind::DiscordStream, false))
            );
            assert_eq!(
                classify_window(process, "Chrome_WidgetWin_1", "Alice's stream", true),
                Some((WindowKind::DiscordStream, true))
            );
            assert_eq!(
                classify_window(
                    process,
                    "Chrome_WidgetWin_1",
                    "Ma Nữ Ponyo | Hexenzirkel • Hội Ma Nữ - Discord",
                    true
                ),
                None
            );
            assert_eq!(
                classify_window(process, "Chrome_WidgetWin_1", "Discord Overlay", true),
                None
            );
            assert_eq!(
                classify_window(process, "Chrome_WidgetWin_1", "Stream của ", true),
                None
            );
        }
        assert_eq!(
            classify_window("notepad", "Chrome_WidgetWin_1", "Stream của thwhnim", true),
            None
        );
        assert_eq!(
            classify_window("discord", "NotElectron", "Stream của thwhnim", true),
            None
        );
        assert_eq!(
            classify_window("chrome", "Chrome_WidgetWin_1", "Picture in picture", true),
            Some((WindowKind::BrowserPip, true))
        );
    }
    #[test]
    fn discord_channel_named_popout_requires_a_distinct_discord_main_window() {
        assert!(is_discord_popout_candidate("Ma Nữ Ponyo"));
        assert!(is_discord_main_title("#welcome | Server - Discord"));
        assert!(!is_discord_popout_candidate("#welcome | Server - Discord"));
        assert!(!is_discord_popout_candidate("Discord Overlay"));
        assert!(!is_discord_popout_candidate("Discord"));
        assert!(verified_discord_popout(
            true, false, false, 795, 473, 1920, 1080
        ));
        assert!(!verified_discord_popout(
            false, false, false, 795, 473, 1920, 1080
        ));
        assert!(!verified_discord_popout(
            true, true, false, 795, 473, 1920, 1080
        ));
        assert!(!verified_discord_popout(
            true, false, true, 795, 473, 1920, 1080
        ));
        assert!(!verified_discord_popout(
            true, false, false, 1920, 1080, 1920, 1080
        ));
    }
    #[test]
    fn settings_backward_compatible() {
        let s: Settings = serde_json::from_str(r#"{"AutoDetect":false,"Opacity":70}"#).unwrap();
        assert!(!s.auto_detect);
        assert!(s.auto_enable);
        assert!(s.auto_discord_streams);
        assert_eq!(s.opacity, 70);
    }
    #[test]
    fn recovery_legacy_field_names() {
        let raw = r#"{"Handle":1,"Pid":2,"ProcessStartTicks":3,"WindowClass":"x","OriginalExStyle":0,"OriginalTopmost":true,"HadLayerAttributes":false,"OriginalColorKey":0,"OriginalAlpha":255,"OriginalLayerFlags":0}"#;
        let r: RecoveryItem = serde_json::from_str(raw).unwrap();
        assert_eq!(r.pid, 2);
    }
}

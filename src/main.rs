#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
mod engine;
mod gui;
mod ipc;
mod service;

fn exclusive_instance() -> Result<windows_sys::Win32::Foundation::HANDLE, String> {
    use windows_sys::Win32::{
        Foundation::{CloseHandle, ERROR_ALREADY_EXISTS, GetLastError},
        System::Threading::CreateMutexW,
    };
    unsafe {
        let name = engine::wide(r"Local\PiPClickThrough_SingleInstance");
        let handle = CreateMutexW(std::ptr::null(), 1, name.as_ptr());
        if handle.is_null() {
            return Err(format!(
                "Cannot create mutex: {}",
                std::io::Error::last_os_error()
            ));
        }
        if GetLastError() == ERROR_ALREADY_EXISTS {
            CloseHandle(handle);
            return Err("PiP ClickThrough is already running (possibly in the tray)".into());
        }
        Ok(handle)
    }
}
fn release_instance(handle: windows_sys::Win32::Foundation::HANDLE) {
    use windows_sys::Win32::{Foundation::CloseHandle, System::Threading::ReleaseMutex};
    unsafe {
        ReleaseMutex(handle);
        CloseHandle(handle);
    }
}

fn main() {
    use windows_sys::Win32::UI::WindowsAndMessaging::{MB_ICONINFORMATION, MB_OK, MessageBoxW};
    let args: Vec<String> = std::env::args().collect();
    if args.len() >= 3 {
        let file = &args[2];
        // Read-only scan can run alongside the GUI; tests/recovery must own
        // the same global mutex so they can never race the live controller.
        let guard = if args[1] == "--recover-only" || args[1] == "--selftest-to" {
            match exclusive_instance() {
                Ok(handle) => Some(handle),
                Err(err) => {
                    let _ = std::fs::write(file, format!("SKIP: {err}"));
                    return;
                }
            }
        } else {
            None
        };
        let output = match args[1].as_str() {
            "--scan-to" => serde_json::to_string_pretty(&engine::scan())
                .unwrap_or_else(|e| format!("scan failed: {e}")),
            "--recover-only" => match engine::Engine::new(&engine::Settings::load()) {
                Ok(mut e) => match e.restore_all() {
                    Ok(_) => "Recovery checked and restore completed".into(),
                    Err(err) => format!("Recovery incomplete: {err}"),
                },
                Err(e) => format!("Recovery failed: {e}"),
            },
            "--selftest-to" => selftest(),
            _ => String::new(),
        };
        if let Some(handle) = guard {
            release_instance(handle);
        }
        if !output.is_empty() {
            if let Err(err) = std::fs::write(file, output) {
                eprintln!("Output failed: {err}");
            }
            return;
        }
    }
    match exclusive_instance() {
        Ok(handle) => {
            if let Err(err) = gui::run() {
                unsafe {
                    MessageBoxW(
                        std::ptr::null_mut(),
                        engine::wide(&format!("{err}")).as_ptr(),
                        engine::wide("PiP ClickThrough - Rust error").as_ptr(),
                        MB_OK,
                    );
                }
            }
            release_instance(handle);
        }
        Err(error) => {
            eprintln!("{error}");
            unsafe {
                MessageBoxW(
                    std::ptr::null_mut(),
                    engine::wide(&error).as_ptr(),
                    engine::wide("PiP ClickThrough").as_ptr(),
                    MB_OK | MB_ICONINFORMATION,
                );
            }
        }
    }
}

fn selftest() -> String {
    let mut messages = vec![format!(
        "Browser topmost candidates: {}",
        engine::scan().len()
    )];
    let found = engine::scan();
    if let Some(target) = found.iter().find(|w| w.strict_match) {
        messages.push(format!("TARGET: {}", target.label()));
        let hwnd = target.handle as isize as windows_sys::Win32::Foundation::HWND;
        let original = engine::exstyle(hwnd);
        let settings = engine::Settings::default();
        match engine::Engine::new(&settings) {
            Ok(mut e) => {
                let result = (|| -> Result<(), String> {
                    e.capture(target).map_err(|err| err.to_string())?;
                    let value = engine::exstyle(hwnd);
                    let bits = 0x08080020u32;
                    if value & bits != bits {
                        return Err(format!("Capture flags not enabled: {value:X}"));
                    }
                    messages.push("Capture flags: PASS".into());
                    e.set_click_through(false);
                    if engine::exstyle(hwnd) & 0x20 != original & 0x20 {
                        return Err("Interactive did not restore original transparent style".into());
                    }
                    messages.push("Interactive: PASS".into());
                    e.set_opacity(75);
                    if engine::exstyle(hwnd) & 0x80000 == 0 {
                        return Err("Opacity layered flag missing".into());
                    }
                    messages.push("Opacity: PASS".into());
                    Ok(())
                })();
                let restored = e.restore_all();
                if let Err(err) = result {
                    messages.push(format!("RESULT: FAILED {err}"));
                } else if let Err(err) = restored {
                    messages.push(format!("RESULT: FAILED restoring: {err}"));
                } else if engine::exstyle(hwnd) & 0x08080020 != original & 0x08080020 {
                    messages.push("RESULT: FAILED original style mismatch".into());
                } else {
                    messages.push("Restore: PASS\nRESULT: COMPLETE".into());
                }
            }
            Err(err) => messages.push(format!("RESULT: FAILED {err}")),
        }
    } else {
        messages.push("SKIP: no strict PiP window currently open.\nRESULT: COMPLETE".into());
    }
    messages.join("\n")
}

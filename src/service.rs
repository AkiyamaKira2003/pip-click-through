//! Thread-hosted Win32 bridge: hotkeys, secure CLI, system tray.
//! No hooks or browser injection; all mutations still happen in Rust's UI thread.
use crate::{engine::wide, ipc};
use eframe::egui;
use std::{
    mem::{size_of, zeroed},
    ptr::{null, null_mut},
    sync::{
        atomic::{AtomicIsize, Ordering},
        mpsc::Sender,
    },
};
use windows_sys::Win32::{
    Foundation::{HWND, LPARAM, LRESULT, POINT, WPARAM},
    System::LibraryLoader::GetModuleHandleW,
    UI::{
        Input::KeyboardAndMouse::{
            MOD_CONTROL, MOD_NOREPEAT, MOD_SHIFT, RegisterHotKey, UnregisterHotKey, VK_F9, VK_F10,
            VK_F12, VK_NEXT, VK_PRIOR,
        },
        Shell::{
            NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NIM_DELETE, NOTIFYICONDATAW, Shell_NotifyIconW,
        },
        WindowsAndMessaging::*,
    },
};

pub enum Event {
    Hotkey(u32),
    Command(Box<ipc::Call>),
    Show,
    Exit,
}
struct Bridge {
    tx: Sender<Event>,
    context: egui::Context,
}
const WM_TRAY: u32 = WM_APP + 14;
const POP_SHOW: usize = 400;
const POP_TOGGLE: usize = 401;
const POP_RESTORE: usize = 402;
const POP_EXIT: usize = 403;
static BRIDGE_HWND: AtomicIsize = AtomicIsize::new(0);

/// Close the hidden tray/hotkey window synchronously so Windows removes
/// the notification icon and unregisters hotkeys before the process exits.
pub fn shutdown() {
    let hwnd = BRIDGE_HWND.swap(0, Ordering::AcqRel);
    if hwnd != 0 {
        unsafe {
            SendMessageW(hwnd as HWND, WM_CLOSE, 0, 0);
        }
    }
}

fn emit(bridge: &Bridge, event: Event) {
    let _ = bridge.tx.send(event);
    bridge.context.request_repaint();
}
unsafe extern "system" fn dispatch(hwnd: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    unsafe {
        if msg == WM_NCCREATE {
            let create = &*(lp as *const CREATESTRUCTW);
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, create.lpCreateParams as isize);
            return 1;
        }
        let pointer = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut Bridge;
        if !pointer.is_null() {
            let bridge = &*pointer;
            match msg {
                WM_HOTKEY => {
                    emit(bridge, Event::Hotkey(wp as u32));
                    return 0;
                }
                ipc::WM_CLI => {
                    let call = Box::from_raw(lp as *mut ipc::Call);
                    emit(bridge, Event::Command(call));
                    return 0;
                }
                WM_TRAY => {
                    match lp as u32 {
                        WM_LBUTTONDBLCLK => emit(bridge, Event::Show),
                        WM_RBUTTONUP | WM_CONTEXTMENU => {
                            let menu = CreatePopupMenu();
                            for (id, name) in [
                                (POP_SHOW, "Open control center"),
                                (POP_TOGGLE, "Toggle click-through"),
                                (POP_RESTORE, "Restore all PiP windows"),
                                (POP_EXIT, "Exit and restore"),
                            ] {
                                AppendMenuW(menu, MF_STRING, id, wide(name).as_ptr());
                            }
                            let mut cursor = POINT::default();
                            GetCursorPos(&mut cursor);
                            SetForegroundWindow(hwnd);
                            TrackPopupMenu(
                                menu,
                                TPM_RIGHTBUTTON,
                                cursor.x,
                                cursor.y,
                                0,
                                hwnd,
                                null(),
                            );
                            DestroyMenu(menu);
                        }
                        _ => {}
                    }
                    return 0;
                }
                WM_COMMAND => {
                    match wp & 0xffff {
                        POP_SHOW => emit(bridge, Event::Show),
                        POP_TOGGLE => emit(bridge, Event::Hotkey(2)),
                        POP_RESTORE => emit(bridge, Event::Hotkey(5)),
                        POP_EXIT => emit(bridge, Event::Exit),
                        _ => {}
                    }
                    return 0;
                }
                WM_DESTROY => {
                    let mut icon: NOTIFYICONDATAW = zeroed();
                    icon.cbSize = size_of::<NOTIFYICONDATAW>() as u32;
                    icon.hWnd = hwnd;
                    icon.uID = 1;
                    Shell_NotifyIconW(NIM_DELETE, &icon);
                    for id in 1..=5 {
                        UnregisterHotKey(hwnd, id);
                    }
                    drop(Box::from_raw(pointer));
                    SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
                    PostQuitMessage(0);
                    return 0;
                }
                _ => {}
            }
        }
        DefWindowProcW(hwnd, msg, wp, lp)
    }
}
pub fn spawn(tx: Sender<Event>, context: egui::Context) {
    std::thread::spawn(move || unsafe {
        let name = wide("PiPClickThrough.Rust.Bridge.2");
        let instance = GetModuleHandleW(null());
        let wc = WNDCLASSW {
            lpfnWndProc: Some(dispatch),
            hInstance: instance,
            lpszClassName: name.as_ptr(),
            ..zeroed()
        };
        if RegisterClassW(&wc) == 0 {
            return;
        }
        let bridge = Box::into_raw(Box::new(Bridge { tx, context }));
        // An invisible ordinary top-level window receives global hotkeys and tray events.
        let hwnd = CreateWindowExW(
            0,
            name.as_ptr(),
            wide("PiP Service").as_ptr(),
            WS_OVERLAPPED,
            0,
            0,
            0,
            0,
            null_mut(),
            null_mut(),
            instance,
            bridge.cast(),
        );
        if hwnd.is_null() {
            drop(Box::from_raw(bridge));
            return;
        }
        BRIDGE_HWND.store(hwnd as isize, Ordering::Release);
        let modifiers = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
        for (id, key) in [
            (1, VK_F9),
            (2, VK_F10),
            (3, VK_PRIOR),
            (4, VK_NEXT),
            (5, VK_F12),
        ] {
            RegisterHotKey(hwnd, id, modifiers, key as u32);
        }
        let mut icon: NOTIFYICONDATAW = zeroed();
        icon.cbSize = size_of::<NOTIFYICONDATAW>() as u32;
        icon.hWnd = hwnd;
        icon.uID = 1;
        icon.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        icon.uCallbackMessage = WM_TRAY;
        icon.hIcon = LoadIconW(null_mut(), IDI_APPLICATION);
        let label = wide("PiP ClickThrough - Rust");
        icon.szTip[..label.len()].copy_from_slice(&label);
        Shell_NotifyIconW(NIM_ADD, &icon);
        let _ = ipc::start(hwnd as isize);
        let mut message: MSG = zeroed();
        while GetMessageW(&mut message, null_mut(), 0, 0) > 0 {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    });
}

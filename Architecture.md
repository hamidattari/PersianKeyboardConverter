# Software Architecture Document: Persian Keyboard Converter

**Version:** 1.0.0  
**Target Platform:** Windows (.NET / WinForms / Win32 / UI Automation)  
**Language:** C#  

---

## Executive Summary

**Persian Keyboard Converter** is a lightweight, background Windows utility designed to seamlessly translate accidentally mis-typed text between English QWERTY and Persian (Farsi ISIRI) keyboard layouts across any active Windows application. Operating silently in the system tray, the application captures a global user-defined hotkey, extracts text from the currently focused control, converts the layout mapping, and writes the corrected text back in real time.

### Core Objectives & Design Goals
* **Non-Intrusive System Integration:** Run in the background via a system tray icon with minimal resource overhead and no mandatory active windows.
* **Universal Application Compatibility:** Support text extraction and replacement in both modern UI Automation-compliant applications and legacy/non-standard controls via fallback mechanisms.
* **Bidirectional Layout Translation:** Effortlessly map character-by-character between English QWERTY and standard Persian layouts, including special character combinations, shifted characters, and numbers.
* **Reliability & Performance:** High-speed processing with safe clipboard preservation and non-blocking background operations.

---

## System Architecture & High-Level Overview

The application follows a modular, layered architecture separating user interface, core domain services, Windows OS interop, and system configuration.

```
+-----------------------------------------------------------------------+
|                            PRESENTATION LAYER                         |
|  +---------------------------+   +---------------+  +--------------+  |
|  |   TrayApplicationContext  |   | SettingsForm  |  | HotkeyPicker |  |
|  +-------------+-------------+   +-------+-------+  +------+-------+  |
+----------------|-------------------------|-----------------|----------+
                 |                         |                 |
+----------------v-------------------------v-----------------v----------+
|                              SERVICE LAYER                            |
|  +--------------------+  +--------------------+  +-----------------+  |
|  |   HotkeyManager    |  |    TextService     |  | SettingsService |  |
|  +---------+----------+  +---------+----------+  +--------+--------+  |
|            |                       |                      |           |
|            |             +---------v----------+           |           |
|            |             |   KeyboardMapper   |           |           |
|            |             +--------------------+           |           |
|            |             +--------------------+           |           |
|            |             |  SpellCheckService  |           |           |
|            |             +---------+----------+           |           |
+------------|----------------------------------------------|-----------+
             |                                              |
+------------v----------------------------------------------v-----------+
|                          OS & PLATFORM INTEROP                        |
|  +--------------------+  +--------------------+  +-----------------+  |
|  | Win32 RegisterHotKey| |   UI Automation    |  |  Win Registry   |  |
|  |   (user32.dll)     |  |  (ValuePattern)    |  |  (Autostart)    |  |
|  +--------------------+  +--------------------+  +-----------------+  |
+-----------------------------------------------------------------------+
```

---

## Core Components Breakdown

### 1. Presentation Layer

#### `TrayApplicationContext` (`TrayApplicationContext.cs`)
* **Role:** Application entry point context and lifetime manager inheriting from `System.Windows.Forms.ApplicationContext`.
* **Key Responsibilities:**
  * Keeps the application process alive without requiring a main visible window.
  * Owns and manages the `NotifyIcon` (system tray icon) and its dynamic context menu (`ContextMenuStrip`).
  * Listens to global hotkey events from `HotkeyManager` and orchestrates text conversion via `TextService`.
  * Provides balloon tip notifications upon completion.
  * Handles fallback icon creation programmatically using `System.Drawing.Bitmap` if an external `.ico` resource is missing.

#### `SettingsForm` (`SettingsForm.cs`), `HotkeyPickerForm` (`HotkeyPickerForm.cs`) & `SuggestionPickerForm` (`SuggestionPickerForm.cs`)
* **Role:** Configuration and correction UI.
* **Key Features:**
  * **Settings Window:** Form override on `OnFormClosing` intercepts `CloseReason.UserClosing` to hide the window to the tray rather than terminating the process.
  * **Interactive Hotkey Capture:** `HotkeyPickerForm` overrides `KeyDown` events to intercept raw key and modifier inputs (Ctrl, Alt, Shift), suppressing standard key press propagation.
  * **Suggestion Picker:** borderless, always-on-top list shown near the caret with the top spelling suggestions. Up to **9 rows are visible at once** — longer lists scroll: the keyboard selection is kept in view (the viewport moves as you navigate past the visible window) and a native scrollbar signals there is more. Uses `WS_EX_NOACTIVATE` (never takes keyboard focus, so the target app keeps the word selected), a drop shadow, owner-drawn items with hover highlighting, and **temporary global hotkeys** registered only while the picker is visible — via its own message-only `NativeWindow` sink — since a non-activating window cannot receive keyboard input directly: **1–9** picks a row, **Esc** cancels, **Ctrl+↑/↓** moves a keyboard selection through the list (wrapping, drawn with an accent bar), and **Enter** applies the selected row (defaulting to the best suggestion). Because the window cannot take focus, Enter/Ctrl+↑↓ are global hotkeys while the picker is open: those keys are consumed system-wide for the picker's short visible lifetime (Esc dismisses instantly), so the user's target-app Enter (e.g. sending a chat message) defers to the picker until it closes. The picker is **per-monitor DPI aware**: the app runs under `PerMonitorV2`, and all pixel metrics are scaled to the DPI of the monitor containing the caret (`GetDpiForMonitor`) while fonts stay in points, so it renders crisply on a secondary monitor with different scaling. Placement clamps to the working area of the monitor that contains the caret (`Screen.FromPoint` — `SystemInformation.WorkingArea` only covers the primary monitor), prefers below-right, **flips above the caret** when there is no room below, and aligns its right edge near the caret when there is no room to the right.

---

### 2. Service & Core Domain Layer

#### `HotkeyManager` (`Services/HotkeyManager.cs`)
* **Role:** Wraps Win32 global hotkey registration capabilities.
* **Technical Design:**
  * Uses P/Invoke to bind `user32.dll` APIs: `RegisterHotKey` and `UnregisterHotKey`.
  * Employs the **Hidden Message-Only Window Pattern** via an internal private class `HotkeyWindow : NativeWindow` to capture `WM_HOTKEY` (`0x0312`) OS messages without stealing UI focus.
  * Manages **two** global hotkeys: the convert hotkey (default `F10`) and the correction hotkey (default `F9`), each with its own `WM_HOTKEY` id and event (`HotkeyPressed` / `CorrectionHotkeyPressed`).
  * Implements `IDisposable` to ensure global hotkeys are properly unregistered when the application exits or rebinds keys.

#### `KeyboardMapper` (`Services/KeyboardMapper.cs`)
* **Role:** Pure static mapping engine for character translation between English QWERTY and Iranian ISIRI Persian keyboard layouts.
* **Translation Mechanics:**
  * **Primary Map (`EnToPe`):** `Dictionary<char, char>` mapping single ASCII keys to Unicode Persian characters (including shift modifiers and Persian digits `۰`–`۹`).
  * **Reverse Map (`PeToEn`):** Inverted lookup dictionary automatically constructed in the static constructor `static KeyboardMapper()`.
  * **Multi-Character Mapping (`EnToPeMulti`):** Handles single English keys that output compound Persian characters (e.g., Shift+B `'B'` -> `"لا"` (`لا`)).
  * **Heuristics:** Includes methods like `IsPersian(char c)` checking UTF-16 ranges (`0x0600–0x06FF`, `0xFB50–0xFDFF`, `0xFE70–0xFEFF`) and `IsMostlyPersian(string)` to detect textual orientation.

#### `TextService` (`Services/TextService.cs`)
* **Role:** Context-aware text extraction and replacement engine using a multi-tiered **Strategy Pattern**.
* **Strategy Execution Pipeline:**
  1. **Primary Strategy (UI Automation `ValuePattern`):**
     * Queries `AutomationElement.FocusedElement`.
     * Checks if `ValuePattern.Pattern` is supported and not read-only.
     * Directly reads and mutates the control value using `ValuePattern.SetValue(...)`. This is instantaneous and bypasses the clipboard entirely.
  2. **Fallback Strategy (Clipboard & Keyboard Simulation):**
     * Executes when UI Automation is unavailable or unsupported (e.g., legacy custom UI frameworks, web browsers without full UIA exposure).
     * Backs up existing clipboard contents.
     * Sends `Ctrl+A` and `Ctrl+C` via `SendKeys.SendWait` to capture focused text.
     * Processes conversion via `KeyboardMapper.Convert(...)`.
     * Places converted text into the clipboard and issues `Ctrl+V`.
     * Restores original clipboard content asynchronously via a dedicated background STA thread (`ApartmentState.STA`) with a safety delay.
     * **Threading:** both the F10 conversion and the F9 correction run on isolated background STA workers, and every clipboard/keyboard sequence is serialized under a global `InputLock` — without it, an F9 worker's modifier-release events could land inside F10's `SendKeys` combo and drop the Ctrl (turning `Ctrl+C` into a stray "c" typed into the target field).
  * **Spell Correction (`CaptureCorrectionProposal` + `ReplaceCorrection`):** reads the selected text or the word around the caret (word boundaries across Persian/English scripts), captures it together with the ranked suggestions from the spelling API, shows a small always-on-top picker near the caret (`SuggestionPickerForm`), and writes the user's chosen suggestion back. Three-tier capture strategy: (1) pure UIA (TextPattern selection/caret + ValuePattern); (2) ValuePattern read + `Ctrl+C` probe + `ValuePattern.SetValue` write-back — for Chromium-style inputs without TextPattern, avoiding a keyboard paste that web-app chat inputs often intercept; (3) clipboard simulation. The UIA-visible selection state is threaded down into the clipboard tier so it **never word-selects over an existing selection** (word-selecting would wipe the user's highlighted text — the rare "F9 selected my text again" bug); the clipboard probe also retries the copy before concluding there is no selection. All tiers run on the background STA worker, so the UI thread is never blocked by the network lookup.

#### `SettingsService` & `AppSettings` (`Services/SettingsService.cs`)
* **Role:** Persistent configuration management.
* **Storage Location:** `%AppData%\PersianKeyboardConverter\settings.json` serialized via `System.Text.Json`.
* **OS Integration:** Interacts with Windows Registry key `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` to enable/disable autostart on Windows logon.

---

## Key Technical Workflows

### Workflow 2: Word Spell-Correction Flow (F9)

```
[ User presses Correction Hotkey (e.g. F9) ]
                 |
                 v
[ OS sends WM_HOTKEY (id = 0xBEEF1) to HotkeyWindow ]
                 |
                 v
[ HotkeyManager fires CorrectionHotkeyPressed ]
                 |
                 v
[ TrayApplicationContext spawns background STA worker ]
                 |
                 v
[ TextService.CaptureCorrectionProposal (STA worker) ]
     |                                |
(Selection present?)            (No selection →
     |                             word at caret)
     v                                v
[ Resolve word range (selected text | word around caret) ]
                 |
                 v
[ SpellCheckService.GetSuggestions(word) → LanguageTool API (fa / en-US) ]
                 |
      (multi-word?)
       /          \
     Yes           No
      |             |
      v             v
[ AutoApply the    [ Any suggestions? ]
  combined fix ]       /           \
                  Yes             No → [ Balloon: "No suggestions found" ]
                   |
                   v
[ UI thread shows SuggestionPickerForm near the caret ]
  (borderless, always-on-top, never steals focus)
                   |
    (click | 1-9 | Ctrl+↑↓ + Enter | Esc = cancel)
                   |
                   v
[ TextService.ReplaceCorrection(proposal, chosen) → ValuePattern splice | clipboard paste ]
                   |
                   v
[ Balloon Tip: "word" → "chosen" (if enabled) ]
```

### Workflow 1: Global Hotkey Trigger & Text Conversion Flow

```
[ User Presses Hotkey (e.g. F10) ]
                 |
                 v
[ OS sends WM_HOTKEY to HotkeyWindow ]
                 |
                 v
[ HotkeyManager fires HotkeyPressed Event ]
                 |
                 v
[ TrayApplicationContext receives Event ]
                 |
      (Is Conversion Enabled?)
             /       \
           Yes        No --> [ Ignore ]
           /
          v
[ TrayApplicationContext spawns background STA worker (guard: one run at a time) ]
          |
          v
[ TextService.ConvertFocusedText() ]
          |
    +-----+-----------------------+
    |                             |
(Try UI Automation ValuePattern)   (Fallback: Clipboard Simulation — under InputLock)
    |                             |
    | [Success]                   | 1. Backup Clipboard
    |                             | 2. Send Ctrl+A, Ctrl+C
    |                             | 3. Read Clipboard & Convert
    |                             | 4. Send Ctrl+V
    |                             | 5. Restore Clipboard (STA Thread)
    +-----+-----------------------+
          |
          v
[ Show Balloon Tip Notification (if enabled) ]
```

---

### `SpellCheckService` (`Services/SpellCheckService.cs`)
* **Role:** Online spelling correction via the free LanguageTool public API (`https://api.languagetool.org/v2/check`, no API key).
* **Behavior:** Picks the language from the dominant script (`fa` for Persian, `en-US` for English). Exposes two entry points:
  * `GetSuggestions(word)` — returns the **full ranked list** of replacements for the match that covers the word entirely (deduplicated, best first) for the suggestion picker.
  * `CorrectText(text)` — applies the best-ranked suggestion for each issue, splicing multi-word offsets last-to-first; returns `null` when the text is correct or the API is unreachable.
* **Limits:** Public endpoint is rate-limited to ~20 requests / 75 KB per IP per minute — ample for an on-demand hotkey.

---

## Design Patterns & Architectural Decisions

| Pattern / Strategy                | Implementation Component          | Purpose & Justification                                                                                                                                                       |
| :-------------------------------- | :-------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Application Context Pattern**   | `TrayApplicationContext`          | Eliminates the need for a hidden main `Form`, managing app lifecycle strictly around background tray interactions.                                                            |
| **Message Window Pattern**        | `HotkeyManager.HotkeyWindow`      | Inherits from `NativeWindow` to create an invisible handle specifically for receiving Win32 message events (`WM_HOTKEY`).                                                     |
| **Strategy Pattern**              | `TextService`                     | Provides a robust fall-through strategy (UIA `ValuePattern` $ ightarrow$ Clipboard Simulation) ensuring maximum compatibility across all Windows desktop apps.                |
| **Static Mapping Service**        | `KeyboardMapper`                  | Thread-safe, high-performance lookup dictionaries initialized once during static class instantiation.                                                                         |
| **Deferred Clipboard Restoration**| `TextService.ConvertViaClipboard` | Uses an isolated STA Thread (`ApartmentState.STA`) with `ThreadPool.QueueUserWorkItem` to prevent blocking the UI thread while waiting to restore original clipboard content. |

---

## Data Structures & Layout Mapping Details

### Character Translation Rules

1. **Direct ASCII to Persian:** Standard lower-case QWERTY keys map to equivalent Persian letters according to standard Iranian keyboard layout (ISIRI 2901 / 9147).
2. **Shifted Punctuation & Diacritics:**
   * `Q` $
ightarrow$ Sukun (`ْ`)
   * `E`, `R`, `T` $
ightarrow$ Tanwin (`ً`, `ٌ`, `ٍ`)
   * `Y`, `U`, `I` $
ightarrow$ Short Vowels / Harakat (`َ`, `ُ`, `ِ`)
   * `O` $
ightarrow$ Shadda (`ّ`)
3. **Compound Key Expansion:** Shift+B (`'B'`) expands into two Unicode characters: `Lām` (`ل`) + `Alef` (`ا`) = `"لا"`.
4. **Number Row Handling:** Converts ASCII digits `'1'`–`'0'` to Persian digits `'۱'`–`'۰'` (`۱`–`۰`).

---

## Security, Reliability & Failure Handling

* **Hotkey Collisions:** If `RegisterHotKey` returns `false` (indicating another application owns the shortcut), `TrayApplicationContext` catches the failure and falls back to registering the default key (`F10`).
* **Clipboard Thread Isolation:** Windows Clipboard APIs require Single-Threaded Apartment (STA) state. The application explicitly initializes `Thread.SetApartmentState(ApartmentState.STA)` when handling background clipboard restoration.
* **Process Privilege Isolation (UI Access):** When target applications are running elevated (as Administrator), UI Automation or `SendKeys` might be restricted by Windows UAC (User Account Control / UIPI). The multi-strategy approach mitigates failure modes where possible.
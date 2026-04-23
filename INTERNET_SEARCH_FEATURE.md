# Internet Search Feature Implementation - Card-Based UI

## Overview
Added a new built-in action "Internet Search" with a **dedicated card-based result window** that uses the Tavily API to search the web for information. When users select text and press the hotkey, they can choose the Internet Search action to get AI-powered answers and search results displayed in beautiful, clickable cards.

## UI/UX Design

### Dedicated Search Result Window
- **Separate window** specifically for internet search results
- **Card-based layout** for better visual hierarchy
- **AI Answer Card** - Highlighted with ✨ icon and purple accent
- **Search Result Cards** - Individual cards for each result with hover effects
- **Clickable cards** - Entire card is clickable to open URL
- **Smooth animations** - Fade-in effects and hover transitions
- **Consistent styling** - Follows existing Spark design system

### Visual Elements
```
┌─────────────────────────────────────────┐
│ 🌐 Internet Search                    ✕ │
│ Query: [user's search query]            │
├─────────────────────────────────────────┤
│                                         │
│ ┌─────────────────────────────────────┐ │
│ │ ✨ AI Answer                        │ │
│ │ [AI-generated answer text...]       │ │
│ └─────────────────────────────────────┘ │
│                                         │
│ 🔗 Search Results                       │
│                                         │
│ ┌─────────────────────────────────────┐ │
│ │ [Result Title]                      │ │
│ │ [URL in blue]                       │ │
│ │ [Content snippet...]                │ │
│ └─────────────────────────────────────┘ │
│                                         │
│ ┌─────────────────────────────────────┐ │
│ │ [Result Title]                      │ │
│ │ [URL in blue]                       │ │
│ │ [Content snippet...]                │ │
│ └─────────────────────────────────────┘ │
│                                         │
├─────────────────────────────────────────┤
│ 5 results found              [Close]    │
└─────────────────────────────────────────┘
```

## Changes Made

### 1. **Windows/InternetSearchResultWindow.xaml** (NEW)
- Dedicated XAML window for internet search results
- Card-based layout with proper spacing and padding
- **AI Answer Card**:
  - Background: `#1E1E2E`
  - Border radius: 12px
  - ✨ icon with purple accent (`#7C6AF7`)
  - Readable text with line height 20
- **Search Result Cards**:
  - Background: `#1E1E2E`
  - Hover background: `#262638`
  - Border radius: 12px
  - Clickable with hand cursor
  - Title in white (`#E0E0F0`)
  - URL in blue (`#6C8AF7`)
  - Content snippet in gray (`#9090B0`)
- **Header**:
  - 🌐 icon with action title
  - Query display
  - Close button
- **Footer**:
  - Result count label
  - Close button
- **Thinking animation**: Bouncing dots while searching
- **Error panel**: User-friendly error display

### 2. **Windows/InternetSearchResultWindow.xaml.cs** (NEW)
- `ShowWithSearch()` - Entry point for displaying search results
- `ProcessSearchAsync()` - Handles Tavily API call and result processing
- `ResultCard_MouseEnter/Leave()` - Hover effects for cards
- `ResultCard_Click()` - Opens URL in default browser
- `BuildResultText()` - Formats results for history
- `TruncateContent()` - Limits snippet length to 150 chars
- Smooth animations (fade-in, scale, translate)
- Analytics and usage tracking
- Error handling with user-friendly messages

### 3. **App.xaml.cs**
- Added `_internetSearchResultWindow` field
- Created and initialized `InternetSearchResultWindow` in `OnStartup()`
- Passed window to `ActionPanelWindow` constructor
- Applied window icon
- Disposed window in `OnExit()`

### 4. **Windows/ActionPanelWindow.xaml.cs**
- Added `_internetSearchResultWindow` field
- Updated constructor to accept `InternetSearchResultWindow` parameter
- Modified `ExecuteSelected()` to call `ShowWithSearch()` for internet search action

### 5. **Windows/ResultWindow.xaml.cs**
- Removed `ShowWithInternetSearch()` method (now using dedicated window)
- Removed `ProcessInternetSearchAsync()` method
- Removed `MakeUrlsClickable()` method
- Cleaned up internet search-related code

## Features

### Card-Based UI
- ✅ **AI Answer Card** - Prominent display with icon and accent color
- ✅ **Search Result Cards** - Individual cards for each result
- ✅ **Hover Effects** - Background color change on hover
- ✅ **Clickable Cards** - Entire card opens URL (not just text)
- ✅ **Hand Cursor** - Visual feedback on hover
- ✅ **Smooth Animations** - Fade-in and scale effects
- ✅ **Scrollable** - Handles many results gracefully

### User Experience
- ✅ **Query Display** - Shows what was searched
- ✅ **Result Count** - Shows number of results found
- ✅ **Truncated Snippets** - Max 150 chars for readability
- ✅ **Top 5 Results** - Focused, not overwhelming
- ✅ **Keyboard Support** - ESC to close
- ✅ **Error Handling** - User-friendly error messages
- ✅ **Loading State** - Thinking animation while searching

### Technical
- ✅ **Dedicated Window** - Separate from AI result window
- ✅ **Modular Design** - Clean separation of concerns
- ✅ **Consistent Styling** - Follows Spark design system
- ✅ **Icon System** - Uses existing icon service
- ✅ **Analytics** - Tracks usage and performance
- ✅ **History** - Persists search results
- ✅ **Diagnostics** - Error tracking and logging

## Color Palette

| Element | Color | Hex |
|---------|-------|-----|
| Background | Dark Gray | `#16161E` |
| Card Background | Darker Gray | `#1E1E2E` |
| Card Hover | Lighter Gray | `#262638` |
| Header/Footer | Dark Blue-Gray | `#1A1A28` |
| Primary Accent | Purple | `#7C6AF7` |
| Title Text | Light Gray | `#E0E0F0` |
| URL Text | Blue | `#6C8AF7` |
| Snippet Text | Medium Gray | `#9090B0` |
| Query Text | Muted Gray | `#8080A8` |

## Typography

| Element | Font Size | Weight | Line Height |
|---------|-----------|--------|-------------|
| Header Icon | 24px | Normal | - |
| Header Title | 16px | SemiBold | - |
| Query Text | 12px | Normal | - |
| Card Header | 13px | SemiBold | - |
| AI Answer | 13px | Normal | 20px |
| Result Title | 14px | SemiBold | - |
| Result URL | 11px | Normal | - |
| Result Snippet | 12px | Normal | 18px |
| Result Count | 12px | Normal | - |

## Spacing & Layout

| Element | Value |
|---------|-------|
| Window Width | 580px |
| Window Height | 620px |
| Border Radius (Window) | 16px |
| Border Radius (Cards) | 12px |
| Card Padding | 16px |
| Card Margin Bottom | 12px |
| Header Padding | 20px 16px |
| Content Padding | 20px 16px |
| Footer Padding | 20px 12px 20px 16px |

## How It Works

### User Flow:
1. User selects text (e.g., "quantum computing")
2. User presses hotkey (Ctrl+Shift+Space)
3. Action panel appears with "Internet Search" (🌐)
4. User clicks "Internet Search" or presses 'W'
5. **Dedicated search window opens** with thinking animation
6. Results appear in card format:
   - AI Answer card at top (if available)
   - Search result cards below
7. User can:
   - Click any card to open URL in browser
   - Scroll through results
   - Press ESC or click Close to dismiss

### Technical Flow:
1. `ActionPanelWindow.ExecuteSelected()` detects `internet_search` action
2. Calls `InternetSearchResultWindow.ShowWithSearch(action, query, cursorPos)`
3. `ProcessSearchAsync()` calls `InternetSearchService.SearchAsync(query)`
4. Tavily API returns answer + search results
5. Results formatted into `SearchResultViewModel` objects
6. Cards rendered with data binding
7. Click handlers attached to each card
8. Analytics and history tracked

## Result Format

### AI Answer Card
```
┌─────────────────────────────────────┐
│ ✨ AI Answer                        │
│                                     │
│ [Concise AI-generated answer       │
│  from Tavily API...]                │
└─────────────────────────────────────┘
```

### Search Result Card
```
┌─────────────────────────────────────┐
│ [Result Title - Bold, White]        │
│ [https://example.com - Blue]        │
│ [Content snippet truncated to       │
│  150 characters for readability...] │
└─────────────────────────────────────┘
```

## Interaction States

### Card States
1. **Default**: Background `#1E1E2E`
2. **Hover**: Background `#262638`, cursor changes to hand
3. **Click**: Opens URL in default browser

### Window States
1. **Loading**: Thinking animation with bouncing dots
2. **Results**: Scrollable card list
3. **Error**: Error icon and message
4. **Empty**: "No results found" message

## Files Modified/Created

- ✅ Created `Windows/InternetSearchResultWindow.xaml`
- ✅ Created `Windows/InternetSearchResultWindow.xaml.cs`
- ✅ Updated `App.xaml.cs`
- ✅ Updated `Windows/ActionPanelWindow.xaml.cs`
- ✅ Updated `Windows/ResultWindow.xaml.cs` (removed internet search code)

## Testing

To test the feature:
1. Build and run Spark
2. Configure Tavily API key in Settings
3. Select any text (e.g., "artificial intelligence")
4. Press Ctrl+Shift+Space
5. Click "Internet Search" or press 'W'
6. Verify:
   - Dedicated window opens
   - AI answer appears in card (if available)
   - Search results appear in individual cards
   - Cards have hover effect
   - Clicking card opens URL in browser
   - ESC closes window
   - Result count shows at bottom

## Future Enhancements
- Add favicon display for each result
- Add "Open All" button to open multiple results
- Add copy button for AI answer
- Add share functionality
- Add search filters (date, domain)
- Add image results display
- Add related searches
- Add search history
- Add keyboard navigation between cards
- Add card animations on load

### 1. **Added Tavily NuGet Package**
- Package: `Tavily` (v1.0.0)
- Added to `Spark.csproj`

### 2. **Models/AppSettings.cs**
- Added `TavilySettings` class with `ApiKey` property
- Added `Tavily` property to `AppSettings` class

### 3. **Services/InternetSearchService.cs** (NEW)
- New service for handling Tavily API calls
- `SearchAsync()` method performs web searches with:
  - Query parameter
  - `include_answer = "basic"` for AI-generated answers
  - `search_depth = "advanced"` for comprehensive results
- Returns `TavilySearchResult` with:
  - AI-generated answer
  - List of search results (URL, title, content, score, favicon)
  - Images (optional)
- Uses Polly retry policy for resilience
- Proper error handling and diagnostics

### 4. **Services/Actions/BuiltInActionCatalogService.cs**
- Added "Internet Search" action:
  - ID: `internet_search`
  - Name: "Internet Search"
  - Icon: 🌐
  - Shortcut: W
  - Description: "Search the web for information"
  - Prompt: `null` (uses Tavily API instead of AI)

### 5. **App.xaml.cs**
- Added `_internetSearchService` field
- Initialized `InternetSearchService` in `OnStartup()`
- Passed service to `ResultWindow` and `ActionPanelWindow` constructors
- Disposed service in `OnExit()`

### 6. **Windows/ActionPanelWindow.xaml.cs**
- Added `_internetSearchService` field
- Updated constructor to accept `InternetSearchService` parameter
- Modified `ExecuteSelected()` to handle `internet_search` action:
  - Checks if service is available
  - Calls `ResultWindow.ShowWithInternetSearch()` instead of AI processing

### 7. **Windows/ResultWindow.xaml.cs**
- Added `_internetSearchService` field
- Updated constructor to accept `InternetSearchService` parameter
- Added `ShowWithInternetSearch()` method:
  - Entry point for internet search actions
  - Similar structure to `ShowWithProcessing()` but for search
- Added `ProcessInternetSearchAsync()` method:
  - Calls Tavily API with the query
  - Formats results with AI answer and top 5 search results
  - Makes URLs clickable in the result window
  - Tracks analytics and usage metrics
- Added `MakeUrlsClickable()` method:
  - Detects clicks on URLs and opens them in default browser
  - Changes cursor to hand when hovering over URLs
  - Uses `System.Windows.Input.Cursors` to avoid ambiguity

### 8. **Services/SettingsService.cs**
- Added `TavilyApiKeyName` constant
- Updated `HydrateSecrets()` to load Tavily API key from DPAPI
- Updated `PersistSecrets()` to save Tavily API key to DPAPI
- Updated `CloneWithoutSecrets()` to exclude Tavily API key from JSON
- Updated `ClearStoredApiKeys()` to remove Tavily API key

### 9. **Windows/SettingsWindow.xaml**
- Added "INTERNET SEARCH" section after Groq settings
- Added `TavilyKeyBox` PasswordBox for Tavily API key input
- Styled consistently with existing provider settings
- Included in the encrypted API keys note

### 10. **Windows/SettingsWindow.xaml.cs**
- Updated `LoadSettings()` to load Tavily API key
- Updated `SaveButton_Click()` to save Tavily API key
- Updated `RotateKeys_Click()` to clear Tavily API key

## How It Works

### User Flow:
1. User selects text (e.g., "water", "quantum computing", "latest news")
2. User presses hotkey (Ctrl+Shift+Space by default)
3. Action panel appears with all actions including "Internet Search" (🌐)
4. User clicks "Internet Search" or presses 'W'
5. Result window shows:
   - **AI Answer**: Concise answer from Tavily
   - **Search Results**: Top 5 results with title, URL, and snippet
6. User can click any URL to open it in their default browser
7. User can copy the entire result or close the window

### Technical Flow:
1. `ActionPanelWindow.ExecuteSelected()` detects `internet_search` action
2. Calls `ResultWindow.ShowWithInternetSearch(action, query, cursorPos)`
3. `ProcessInternetSearchAsync()` calls `InternetSearchService.SearchAsync(query)`
4. Tavily API returns answer + search results
5. Results formatted and displayed in `ResultWindow`
6. URLs made clickable with hover effects
7. Analytics and usage metrics tracked

## API Key Storage
- Tavily API key stored securely using Windows DPAPI (Data Protection API)
- Same security model as OpenRouter and Groq API keys
- Keys encrypted per-user, per-machine
- Not stored in plain text in `settings.json`

## Configuration
Users need to:
1. Get a Tavily API key from [tavily.com](https://tavily.com)
2. Open Spark Settings (right-click tray icon → Settings)
3. Scroll to "INTERNET SEARCH" section
4. Enter Tavily API key
5. Click Save

## Result Format
```
📝 Answer:
[AI-generated answer from Tavily]

🔗 Search Results:

1. [Title]
   [URL - clickable]
   [Content snippet...]

2. [Title]
   [URL - clickable]
   [Content snippet...]

... (up to 5 results)
```

## Features
- ✅ Secure API key storage (DPAPI)
- ✅ AI-generated answers
- ✅ Top 5 search results with snippets
- ✅ Clickable URLs that open in default browser
- ✅ Hand cursor on URL hover
- ✅ Analytics tracking
- ✅ Usage metrics
- ✅ History persistence
- ✅ Copy/Replace functionality
- ✅ Error handling with user-friendly messages
- ✅ Retry policy for network resilience
- ✅ Consistent UI/UX with existing actions

## Icon System
- Uses 🌐 (globe) emoji for Internet Search action
- Follows existing icon system in `IconService`
- Consistent with other built-in actions

## Modular Structure
- New service (`InternetSearchService`) follows existing service pattern
- No modifications to core functionality
- Optional dependency (gracefully handles missing API key)
- Can be easily extended for other search providers
- Follows existing error handling and diagnostics patterns

## Testing
To test the feature:
1. Build and run Spark
2. Configure Tavily API key in Settings
3. Select any text (e.g., "artificial intelligence")
4. Press Ctrl+Shift+Space
5. Click "Internet Search" or press 'W'
6. Verify AI answer and search results appear
7. Click a URL to verify it opens in browser
8. Test Copy and Replace buttons

## Future Enhancements
- Add search depth configuration (basic/advanced)
- Add number of results configuration
- Add image results display
- Add follow-up questions support
- Add search history
- Add search filters (date, domain, etc.)

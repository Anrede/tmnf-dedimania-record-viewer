# TMNF Dedimania Record Viewer

A WPF desktop application for extracting and displaying TMNF Dedimania online records from a TMNF Exchange track page.

## Features

- Loads a TMNF Exchange track page inside the application
- Uses an embedded WebView2 browser
- Extracts the Dedimania online records table from the rendered page
- Displays record data in a grid
- Extracts rank, time, mode, player name, and server information
- Shows text segment and style details
- Exports extracted data as JSON

## Technologies Used

- C#
- WPF
- .NET 8
- WebView2
- JavaScript DOM extraction
- JSON export

## How It Works

The application loads a TMNF Exchange track page through WebView2.  
After the page is rendered, a JavaScript extraction script reads the Dedimania online records table from the DOM and returns the parsed data to the WPF application.

## How to Run

1. Clone the repository.
2. Open the project in Visual Studio 2022 or later.
3. Restore NuGet packages.
4. Make sure WebView2 Runtime is installed.
5. Run the application.

## Project Status

This project was developed iteratively.  
The first version focused on extracting and displaying Dedimania online records. Later versions improved the UI, added import/export features, offline records, leaderboard customization, settings, hotkeys, animations, and advanced layout controls.

## Author

Yusuf SARISOY
# CMakeLists.txt for Linux Restore Application

cmake_minimum_required(VERSION 3.10)
project(BackupRestoreLinux VERSION 4.6.0)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find required packages
find_package(Curses REQUIRED)
find_package(PkgConfig)

# Restore Engine Library
add_library(restore_engine STATIC
    restore_engine.cpp
)

target_link_libraries(restore_engine
    stdc++fs  # Filesystem library
)

# Terminal UI Application (ncurses TUI)
add_executable(restore_tui
    restore_tui.cpp
)

target_link_libraries(restore_tui
    restore_engine
    ${CURSES_LIBRARIES}
    ncurses
    menu
)

# CLI Application (simpler, no ncurses)
add_executable(restore_cli
    restore_cli.cpp
)

target_link_libraries(restore_cli
    restore_engine
)

# Graphical UI Application (GTK+, optional)
if(PKG_CONFIG_FOUND)
    pkg_check_modules(GTK3 gtk+-3.0)
    
    if(GTK3_FOUND)
        message(STATUS "GTK+ 3 found - Building GUI version")
        
        add_executable(restore_gui
            restore_gui_gtk.cpp
        )
        
        target_include_directories(restore_gui PRIVATE ${GTK3_INCLUDE_DIRS})
        target_link_directories(restore_gui PRIVATE ${GTK3_LIBRARY_DIRS})
        target_link_libraries(restore_gui
            restore_engine
            ${GTK3_LIBRARIES}
        )
        
        target_compile_options(restore_gui PRIVATE ${GTK3_CFLAGS_OTHER})
        
        install(TARGETS restore_gui RUNTIME DESTINATION bin)
    else()
        message(STATUS "GTK+ 3 not found - Skipping GUI version (install libgtk-3-dev)")
    endif()
endif()

# Installation
install(TARGETS restore_tui restore_cli
    RUNTIME DESTINATION bin
)

install(FILES
    README.md
    LICENSE
    DESTINATION share/doc/backup-restore
)


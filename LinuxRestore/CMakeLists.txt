# CMakeLists.txt for Linux Restore Application
# Version 5.13.7.0 - Added WIM (.ssb) support via wimlib

cmake_minimum_required(VERSION 3.10)
project(SecureServerBackupLinuxRestore VERSION 5.13.7)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(SSB_RESTORE_TUI_NAME SecureServerBackupLinuxRestore)
set(SSB_RESTORE_CLI_NAME SecureServerBackupLinuxRestoreCli)
set(SSB_RESTORE_GUI_NAME SecureServerBackupLinuxRestoreGui)
option(SSB_RESTORE_BUILD_TUI "Build the ncurses-based Linux restore TUI" ON)

# Find required packages
find_package(PkgConfig)

if(SSB_RESTORE_BUILD_TUI)
    find_package(Curses REQUIRED)
endif()

# NOTE: WIM support requires wimlib-imagex to be installed on the target system
# This is a runtime dependency, not a compile-time dependency
# Debian/Ubuntu: sudo apt-get install wimtools
# Fedora/RHEL:   sudo dnf install wimlib-utils
# Arch Linux:    sudo pacman -S wimlib

# Restore Engine Library
add_library(restore_engine STATIC
    restore_engine.cpp
)

target_link_libraries(restore_engine
    stdc++fs  # Filesystem library
)

# Terminal UI Application (ncurses TUI)
if(SSB_RESTORE_BUILD_TUI)
    add_executable(${SSB_RESTORE_TUI_NAME}
        restore_tui.cpp
    )

    target_link_libraries(${SSB_RESTORE_TUI_NAME}
        restore_engine
        ${CURSES_LIBRARIES}
        ncurses
        menu
    )
else()
    message(STATUS "Skipping ncurses TUI build (SSB_RESTORE_BUILD_TUI=OFF)")
endif()

# CLI Application (simpler, no ncurses)
add_executable(${SSB_RESTORE_CLI_NAME}
    restore_cli.cpp
)

target_link_libraries(${SSB_RESTORE_CLI_NAME}
    restore_engine
)

add_executable(restore_engine_tests
    restore_engine_tests.cpp
)

target_link_libraries(restore_engine_tests
    stdc++fs
)

# Graphical UI Application (GTK+, optional)
if(PKG_CONFIG_FOUND)
    pkg_check_modules(GTK3 gtk+-3.0)
    
    if(GTK3_FOUND)
        message(STATUS "GTK+ 3 found - Building GUI version")
        
        add_executable(${SSB_RESTORE_GUI_NAME}
            restore_gui_gtk.cpp
        )
        
        target_include_directories(${SSB_RESTORE_GUI_NAME} PRIVATE ${GTK3_INCLUDE_DIRS})
        target_link_directories(${SSB_RESTORE_GUI_NAME} PRIVATE ${GTK3_LIBRARY_DIRS})
        target_link_libraries(${SSB_RESTORE_GUI_NAME}
            restore_engine
            ${GTK3_LIBRARIES}
        )
        
        target_compile_options(${SSB_RESTORE_GUI_NAME} PRIVATE ${GTK3_CFLAGS_OTHER})
        
        install(TARGETS ${SSB_RESTORE_GUI_NAME} RUNTIME DESTINATION bin)
    else()
        message(STATUS "GTK+ 3 not found - Skipping GUI version (install libgtk-3-dev)")
    endif()
endif()

# Installation
set(SSB_RESTORE_INSTALL_TARGETS ${SSB_RESTORE_CLI_NAME})

if(SSB_RESTORE_BUILD_TUI)
    list(APPEND SSB_RESTORE_INSTALL_TARGETS ${SSB_RESTORE_TUI_NAME})
endif()

install(TARGETS ${SSB_RESTORE_INSTALL_TARGETS}
    RUNTIME DESTINATION bin
)

install(FILES
    README.md
    LICENSE
    DESTINATION share/doc/backup-restore
)


LOCAL_PATH := $(call my-dir)

include $(CLEAR_VARS)
LOCAL_MODULE    := tun_launcher
LOCAL_SRC_FILES := tun_launcher.c
LOCAL_LDLIBS    := -llog
include $(BUILD_SHARED_LIBRARY)
#include <unistd.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>
#include <signal.h>
#include <sys/wait.h>
#include <android/log.h>

#define TAG "tun_launcher"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

// Целевой номер fd для TUN в дочернем процессе — маленький номер чтобы избежать проблем
#define TUN_TARGET_FD 10

int launch_tun2socks(int fd, const char* bin, const char* proxy) {
    LOGI("launch_tun2socks: fd=%d bin=%s proxy=%s", fd, bin, proxy);

    // Pipe для чтения вывода дочернего процесса
    int pipefd[2];
    if (pipe(pipefd) < 0) {
        LOGE("pipe failed: %d", errno);
        return -1;
    }

    pid_t pid = fork();
    if (pid < 0) {
        LOGE("fork failed: %d", errno);
        close(pipefd[0]);
        close(pipefd[1]);
        return -1;
    }

    if (pid == 0) {
        // === ДОЧЕРНИЙ ПРОЦЕСС ===
        close(pipefd[0]);

        // Перенаправляем stdout/stderr в pipe
        dup2(pipefd[1], STDOUT_FILENO);
        dup2(pipefd[1], STDERR_FILENO);
        close(pipefd[1]);

        // Копируем TUN fd на TUN_TARGET_FD (маленький номер)
        // Это важно: большие номера fd могут не наследоваться корректно
        if (fd != TUN_TARGET_FD) {
            if (dup2(fd, TUN_TARGET_FD) < 0) {
                fprintf(stderr, "dup2(%d, %d) failed: %d\n", fd, TUN_TARGET_FD, errno);
                _exit(1);
            }
            close(fd);
        }

        // Устанавливаем O_NONBLOCK на целевой fd
        int flags = fcntl(TUN_TARGET_FD, F_GETFL, 0);
        if (flags >= 0) {
            fcntl(TUN_TARGET_FD, F_SETFL, flags | O_NONBLOCK);
        }

        // Формируем строку fd://N
        char fd_str[64];
        snprintf(fd_str, sizeof(fd_str), "fd://%d", TUN_TARGET_FD);

        fprintf(stderr, "execl: %s -device %s -proxy %s\n", bin, fd_str, proxy);
        fflush(stderr);

        execl(bin, bin,
              "-device",   fd_str,
              "-proxy",    proxy,
              "-loglevel", "debug",
              NULL);

        fprintf(stderr, "execl failed: %d\n", errno);
        fflush(stderr);
        _exit(1);
    }

    // === РОДИТЕЛЬСКИЙ ПРОЦЕСС ===
    close(pipefd[1]);

    // Запускаем logger в отдельном fork
    pid_t logger_pid = fork();
    if (logger_pid == 0) {
        char buf[512];
        int n;
        while ((n = read(pipefd[0], buf, sizeof(buf) - 1)) > 0) {
            buf[n] = '\0';
            // Убираем trailing newline
            if (n > 0 && buf[n-1] == '\n') buf[n-1] = '\0';
            LOGI("[tun2socks] %s", buf);
        }
        close(pipefd[0]);

        int status;
        waitpid(pid, &status, 0);
        LOGI("tun2socks exited, code=%d", WEXITSTATUS(status));
        _exit(0);
    }
    close(pipefd[0]);

    LOGI("tun2socks launched, pid=%d", pid);
    return pid;
}

void kill_tun2socks(int pid) {
    if (pid > 0) {
        kill(pid, SIGTERM);
        LOGI("sent SIGTERM to pid=%d", pid);
    }
}
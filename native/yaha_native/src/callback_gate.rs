use std::sync::{Arc, Condvar, Mutex};

#[derive(Clone, Debug, Default)]
pub struct CallbackGate(Arc<Inner>);

#[derive(Debug, Default)]
struct Inner {
    state: Mutex<State>,
    idle: Condvar,
}

#[derive(Debug, Default)]
struct State {
    closed: bool,
    active: usize,
}

pub struct CallbackGuard(Arc<Inner>);

impl CallbackGate {
    pub fn enter(&self) -> Option<CallbackGuard> {
        let mut state = self.0.state.lock().unwrap();
        if state.closed {
            return None;
        }
        state.active += 1;
        Some(CallbackGuard(self.0.clone()))
    }

    pub fn close(&self) {
        self.0.state.lock().unwrap().closed = true;
    }

    pub fn wait(&self) {
        let mut state = self.0.state.lock().unwrap();
        assert!(state.closed);
        while state.active != 0 {
            state = self.0.idle.wait(state).unwrap();
        }
    }
}

impl CallbackGuard {
    pub fn retain_for_deferred_ack(&self) -> Self {
        self.0.state.lock().unwrap().active += 1;
        Self(self.0.clone())
    }
}

impl Drop for CallbackGuard {
    fn drop(&mut self) {
        let mut state = self.0.state.lock().unwrap();
        state.active -= 1;
        if state.active == 0 {
            self.0.idle.notify_all();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{sync::mpsc, thread, time::Duration};

    const TIMEOUT: Duration = Duration::from_secs(5);

    #[test]
    fn close_rejects_new_callbacks_and_waits_for_an_admitted_callback() {
        let gate = CallbackGate::default();
        let guard = gate.enter().unwrap();
        gate.close();
        assert!(gate.enter().is_none());
        let stopping = gate.clone();
        let (done_tx, done_rx) = mpsc::channel();
        let waiter = thread::spawn(move || {
            stopping.wait();
            done_tx.send(()).unwrap();
        });
        assert!(matches!(
            done_rx.recv_timeout(Duration::from_millis(50)),
            Err(mpsc::RecvTimeoutError::Timeout)
        ));
        drop(guard);
        done_rx.recv_timeout(TIMEOUT).unwrap();
        waiter.join().unwrap();
        gate.close();
        gate.wait();
    }

    #[test]
    fn deferred_acknowledgement_outlives_callback_even_when_close_races_it() {
        let gate = CallbackGate::default();
        let guard = gate.enter().unwrap();
        gate.close();
        let acknowledgement = guard.retain_for_deferred_ack();
        drop(guard);
        let stopping = gate.clone();
        let (done_tx, done_rx) = mpsc::channel();
        let waiter = thread::spawn(move || {
            stopping.wait();
            done_tx.send(()).unwrap();
        });
        assert!(matches!(
            done_rx.recv_timeout(Duration::from_millis(50)),
            Err(mpsc::RecvTimeoutError::Timeout)
        ));
        drop(acknowledgement);
        done_rx.recv_timeout(TIMEOUT).unwrap();
        waiter.join().unwrap();
    }

    #[test]
    fn closing_one_handler_does_not_close_another_or_join_native_work() {
        let runtime = tokio::runtime::Runtime::new().unwrap();
        let gate = CallbackGate::default();
        let other = CallbackGate::default();
        let late_callback = gate.clone();
        let (entered_tx, entered_rx) = mpsc::channel();
        let (release_tx, release_rx) = mpsc::channel();
        let work = runtime.spawn_blocking(move || {
            entered_tx.send(()).unwrap();
            release_rx.recv_timeout(TIMEOUT).unwrap();
            assert!(late_callback.enter().is_none());
        });
        entered_rx.recv_timeout(TIMEOUT).unwrap();
        gate.close();
        gate.wait();
        assert!(!work.is_finished());
        assert!(other.enter().is_some());
        release_tx.send(()).unwrap();
        runtime.block_on(work).unwrap();
    }
}

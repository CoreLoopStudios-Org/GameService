/**
 * Game Sound Effects - Simple oscillator-based sounds (no audio files needed)
 * Uses Web Audio API for instant, lightweight sound generation
 */
window.GameSounds = {
    audioContext: null,

    init: function () {
        if (this.audioContext) return;
        this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
    },

    play: function (soundName) {
        if (!this.audioContext) this.init();
        if (this.audioContext.state === 'suspended') {
            this.audioContext.resume();
        }

        switch (soundName) {
            case 'diceRoll':
                this._playTone(400, 0.1, 'square');
                setTimeout(() => this._playTone(500, 0.1, 'square'), 50);
                setTimeout(() => this._playTone(600, 0.15, 'square'), 100);
                break;
            case 'coinWin':
                this._playTone(523, 0.1, 'sine');
                setTimeout(() => this._playTone(659, 0.1, 'sine'), 80);
                setTimeout(() => this._playTone(784, 0.2, 'sine'), 160);
                break;
            case 'playerJoined':
                this._playTone(440, 0.15, 'sine');
                setTimeout(() => this._playTone(554, 0.15, 'sine'), 100);
                break;
            case 'tokenCaptured':
                this._playTone(300, 0.15, 'sawtooth');
                setTimeout(() => this._playTone(200, 0.2, 'sawtooth'), 100);
                break;
            case 'gameWon':
                this._playChord([523, 659, 784], 0.3, 'sine');
                setTimeout(() => this._playChord([587, 740, 880], 0.4, 'sine'), 300);
                break;
            case 'turnTimeout':
                this._playTone(200, 0.3, 'square');
                break;
            case 'chatMessage':
                this._playTone(800, 0.05, 'sine');
                break;
        }
    },

    _playTone: function (frequency, duration, type) {
        const oscillator = this.audioContext.createOscillator();
        const gainNode = this.audioContext.createGain();

        oscillator.type = type || 'sine';
        oscillator.frequency.setValueAtTime(frequency, this.audioContext.currentTime);

        gainNode.gain.setValueAtTime(0.3, this.audioContext.currentTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, this.audioContext.currentTime + duration);

        oscillator.connect(gainNode);
        gainNode.connect(this.audioContext.destination);

        oscillator.start(this.audioContext.currentTime);
        oscillator.stop(this.audioContext.currentTime + duration);
    },

    _playChord: function (frequencies, duration, type) {
        frequencies.forEach(freq => this._playTone(freq, duration, type));
    }
};

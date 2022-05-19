'use strict';

// Don't require these until we've hooked certain builtins
var ChildProcess,
    dgramSocket,
    HttpServer,
    HttpsServer,
    cluster,
    path,
    Server,
    Socket,
    Timer,
    TlsServer,
    async_hooks;

var NODE_VERSION = process.version.slice(1).split('.').map(function (v) { return parseInt(v, 10); });

var DONT_INSTRUMENT = {
    'ChildProcess': NODE_VERSION[0] === 0 && NODE_VERSION[1] <= 10
};

var _console_log = console.log.bind(console),
    _console_warn = console.warn.bind(console),
    _console_error = console.error.bind(console);

function timerCallback(thing) {
    if (typeof thing._repeat === 'function') { return '_repeat'; }
    if (typeof thing._onTimeout === 'function') { return '_onTimeout'; }
    if (typeof thing._onImmediate === 'function') { return '_onImmediate'; }
}

var log = (function () {
    var util;
    var fns = { };

    function log(/*type, args...*/) {
        // lazy load so we can define this early but don't cause any requires
        // until after we've hooked things or don't care
        util = util || require('util');
        var i = arguments.length, args = new Array(i);
        while (i--) { args[i] = arguments[i]; }
        var type = args.shift(), str = util.format.apply(util, args);
        return fns[type](str);
    }
    log.setLogger = function setLogger(type, fn) {
        fns[type] = fn;
    };
    log.resetLoggers = function resetLoggers() {
        fns = {
            'info': _console_log,
            'warn': _console_warn,
            'error': _console_error
        };
    };

    log.resetLoggers();
    return log;
})();

var trackedAsyncResources = null, refSymbol = null;
function getRefSymbol() {
    function _noop() { }
    function _getRefSymbol(asyncId, type, triggerAsyncId, resource) {
        var symbols = Object.getOwnPropertySymbols(resource);
        symbols.forEach(function (sym) {
            if (sym.description === 'refed') {
                refSymbol = sym;
            }
        });
    }
    var hook = async_hooks.createHook({
        init: _getRefSymbol,
        destroy: _noop // node 16.2.0+ crashes without this
    });
    hook.enable();
    var timer = setTimeout(_noop, 1000);
    hook.disable();
    clearTimeout(timer);
}
function setupAsyncHooks() {
    async_hooks = require('async_hooks');
    getRefSymbol();
    trackedAsyncResources = new Map();

    function init(asyncId, type, triggerAsyncId, resource) {
        if (type === 'Timeout') {

            trackedAsyncResources.set(asyncId, {
                resource: resource,
                type: type
            });
        }

    }
    function destroy(asyncId) {
        if (trackedAsyncResources.has(asyncId)) {
            trackedAsyncResources.delete(asyncId);
        }
    }
    var hook = async_hooks.createHook({
        init: init,
        destroy: destroy
    });
    hook.enable();
}

// hook stuff
(function () {
    var getStackFrames = function (_, stack) { return stack; };
    var getMappedStackFrames = function (_, stack) {
        // attempt to use source-map-support to process any source map info

        // This is copy-pasted from part of source-map-support's implementation
        // of prepareStackTrace. We're interested in the data, which we filter
        // for the first/likely relevant call site. We don't want to un-stringify
        // and parse the (string) return value of prepareStackTrace itself.
        var state = { nextPosition: null, curPosition: null };

        for (var i = stack.length - 1; i >= 0; i--) {
            stack[i] = sms.wrapCallSite(stack[i], state);
            state.nextPosition = state.curPosition;
        }
        state.curPosition = state.nextPosition = null;
        return stack;
    }

    var sms = null;
    // we don't want to load any modules until we've hooked everything
    // this function defers loading `source-map-support`, and also tests
    // that it functions as expected. since we don't directly depend on
    // that module, it could have any version and the api could break in
    // the future. if that happens, we just behave as though it wasn't
    // present, and the user's code will continue to be mapped -- only
    // wtfnode source line attribution will not be.
    function loadSMS() {
        if (sms !== null) { return; }
        var _Error_prepareStackTrace = Error.prepareStackTrace;
        try {
            sms = require('source-map-support');
        } catch (e) {
            return;
        }
        try {
            Error.prepareStackTrace = getMappedStackFrames;
            (new Error('synthetic')).stack;
            Error.prepareStackTrace = _Error_prepareStackTrace;
            getStackFrames = getMappedStackFrames;
            return;
        } catch (e) {
            Error.prepareStackTrace = _Error_prepareStackTrace;
            sms = false;
            log('warn', 'error getting source-mapped stack -- did the api change?');
        }
    }

    function getStack() {
        loadSMS();

        // capture whatever the current prepareStackTrace is when we call this function...
        var _Error_prepareStackTrace = Error.prepareStackTrace;
        Error.prepareStackTrace = getStackFrames;
        var unprocessedStack = (new Error('synthetic')).stack;
        // set it back ASAP so any failures are handled normally
        Error.prepareStackTrace = _Error_prepareStackTrace;

        return unprocessedStack.map(function (item) {
            if (item.isEval()) {
                var matched = item.getEvalOrigin().match(/\((.*):(\d*):(\d*)\)/) || {};
                return {
                    name: '<eval>',
                    file: matched[1],
                    line: matched[2]
                };
            }
            return {
                name: item.getFunctionName(),
                file: item.getFileName(),
                line: item.getLineNumber()
            };
        });
    }

    function findCallsite(stack) {
        for (var i = 0; i < stack.length; i++) {
            // Ignore frames from:
            //  - null/undefined values
            //  - wtfnode by excluding __filename
            //  - builtins by excluding files with no path separator
            //  - internal builtins by excluding files beginning with 'internal/'
            //    (even on windows, the stack trace uses unix separators for these)
            if (stack[i].file &&
                stack[i].file !== __filename &&
                stack[i].file.indexOf(path.sep) !== -1 &&
                stack[i].file.slice(0, 9) !== 'internal/'
            ) {
                return stack[i];
            }
        }
        return null;
    }

    function copyProperties(source, target) {
        // this should inherit 'name' and 'length' and any other properties that have been assigned
        Object.getOwnPropertyNames(source).forEach(function (key) {
            try {
                Object.defineProperty(target, key, Object.getOwnPropertyDescriptor(source, key));
            } catch (e) {
                // some properties cannot be redefined, not much we can do about it
            }
        });
        if (!Object.getOwnPropertySymbols) { return; }
        Object.getOwnPropertySymbols(source).forEach(function (sym) {
            try {
                Object.defineProperty(target, sym, Object.getOwnPropertyDescriptor(source, sym));
            } catch (e) {
                // some properties cannot be redefined, not much we can do about it
            }
        });
    }

    // wraps a function with a proxy function holding the first userland call
    // site in the stack and some other information, for later display
    // this will probably screw up any code that depends on the callbacks having
    // a 'name' or 'length' property that is accurate, but there doesn't appear
    // to be a way around that :(
    function wrapFn(fn, name, isInterval, callback) {
        if (typeof fn !== 'function') { return fn; }

        var wrapped = (
            typeof callback === 'function' ?
            function () {
                callback.call(this, wrapped);
                return fn.apply(this, arguments);
            }
            :
            function () {
                return fn.apply(this, arguments);
            }
        );

        var stack = getStack();

        // this should inherit 'name' and 'length' and any other properties that have been assigned
        // also inherits prototype, and symbols like the promisify value!
        copyProperties(fn, wrapped);

        // we use these later to identify the source information about an open handle
        if (!wrapped.hasOwnProperty('__callSite')) {
            Object.defineProperties(wrapped, {
                __fullStack: {
                    enumerable: false,
                    configurable: false,
                    writable: false,
                    value: stack
                },
                __name: {
                    enumerable: false,
                    configurable: false,
                    writable: false,
                    value: name || '(anonymous)'
                },
                __callSite: {
                    enumerable: false,
                    configurable: false,
                    writable: false,
                    value: findCallsite(stack)
                },
                __isInterval: {
                    enumerable: false,
                    configurable: false,
                    writable: false,
                    value: isInterval
                }
            });
        }
        return wrapped;
    }

    var GLOBALS = { };
    function wrapTimer(type, isInterval) {
        GLOBALS[type] = global[type];
        global[type] = function () {
            var args = [ ], i = arguments.length;
            while (i--) { args[i] = arguments[i]; }

            var ret = GLOBALS[type].apply(this, args);
            var cbkey = timerCallback(ret);
            if (ret[cbkey]) {
                ret[cbkey] = wrapFn(ret[cbkey], args[0].name, isInterval);
            }

            return ret;
        };
        copyProperties(GLOBALS[type], global[type]);
    };
    wrapTimer('setTimeout', false);
    wrapTimer('setInterval', true);

    var EventEmitter = require('events').EventEmitter,
        Readable = require('stream').Readable;

    var _EventEmitter_init = EventEmitter.init;
    var _EventEmitter_listeners = EventEmitter.prototype.listeners;

    if (!DONT_INSTRUMENT['ChildProcess']) {
        // this will conveniently be run on new child processes
        EventEmitter.init = function () {
            var callSite = findCallsite(getStack());
            if (callSite && !this.hasOwnProperty('__callSite')) {
                Object.defineProperties(this, {
                    __callSite: {
                        enumerable: false,
                        configurable: false,
                        writable: false,
                        value: findCallsite(getStack())
                    }
                });
            }

            return _EventEmitter_init.apply(this, arguments);
        };
    }

    function addListener(emitter, origMethod, type, cb) {
        var before = _EventEmitter_listeners.call(emitter, type);
        var ret = origMethod.call(emitter, type, cb);
        var after = _EventEmitter_listeners.call(emitter, type);
        var newListeners = after.filter(function(handler) {
            return before.indexOf(handler) === -1;
        });
        return [ret, newListeners];
    }

    // Readable streams have their own handling of the "on data" event
    // which must be wrapped separately; if we call EventEmitter's handler
    // on a readable stream, it will do the wrong thing
    [EventEmitter, Readable].forEach(function (Cls) {
        ['on', 'addListener', 'once'].forEach(function (method) {
            var origMethod = Cls.prototype[method];
            Cls.prototype[method] = function (/*type, listener*/) {
                var args = [ ], i = arguments.length, fn;
                while (i--) { args[i] = arguments[i]; }

                var type = args[0], fn = args[1];

                var callSite = wrapFn(args[1], args[1].name, null).__callSite;

                var res = addListener(this, origMethod, type, fn);
                var ret = res[0], newListeners = res[1];
                newListeners.forEach(function (listener) {
                    // I've tried to avoid mutating anything that's not mine, however
                    // the possibilities across Node versions, wrapping behavior, and
                    // so on made this complex. So instead, we just tag all possible
                    // wrappers and functions with the callsite and move on with our
                    // life. this lets node behave however it would behave, and whatever
                    // we wind up with a reference to (wrapped or unwrapped, etc.)
                    // in getActiveHandles, it should have the info we want.
                    while (listener && !listener.hasOwnProperty('__callSite')) {
                        Object.defineProperties(listener, {
                            __callSite: {
                                enumerable: false,
                                configurable: false,
                                writable: false,
                                value: callSite
                            }
                        });
                        listener = listener.listener;
                    }
                });
                return ret;
            };
        });
    });

    // path must be required before the rest of these
    // as some of them invoke our hooks on load which
    // requires path to be available to the above code
    path = require('path');

    dgramSocket = require('dgram').Socket;
    HttpServer = require('http').Server;
    HttpsServer = require('https').Server;
    Server = require('net').Server;
    Socket = require('net').Socket;
    if (NODE_VERSION[0] < 11) {
        Timer = process.binding('timer_wrap').Timer;
    } else {
        setupAsyncHooks();
    }
    TlsServer = require('tls').Server;

    ChildProcess = (function () {
        var ChildProcess = require('child_process').ChildProcess;

        if (typeof ChildProcess !== 'function') {
            // node 0.10 doesn't expose the ChildProcess constructor, so we have to get it on the sly
            var cp = require('child_process').spawn('true', [], { stdio: 'ignore' });
            ChildProcess = cp.constructor;
        }

        return ChildProcess;
    })();

    cluster = require('cluster');
    var _cluster_fork = cluster.fork;
    cluster.fork = function (/*env*/) {
        var worker = _cluster_fork.apply(this, arguments);

        // we get an open handle for a pipe, but no reference to the
        // worker itself, so we add one, as well as the call site info
        if (worker && worker.process && worker.process._channel) {
            Object.defineProperties(worker.process._channel, {
                __callSite: {
                    enumerable: false,
                    configurable: false,
                    writable: false,
                    value: findCallsite(getStack())
                },
                __worker: {
                    enumerable: false,
                    configurable: false,
                    writable: false,
                    value: worker
                }
            });
        }

        return worker;
    };
})();

function formatTime(t) {
    var labels = ['ms', 's', 'min', 'hr'],
        units = [1, 1000, 60, 60],
        i = 0;

    while (i < units.length && t / units[i] > 1) { t /= units[i++]; }
    return Math.floor(t) + ' ' + labels[i-1];
};

function getCallsite(thing) {
    if (!thing.__callSite) {
        var name = ((thing.name ? thing.name : thing.constructor.name) || '(anonymous)').trim();
        if (!DONT_INSTRUMENT[name]) {
            log('warn', 'Unable to determine callsite for "'+name+'". Did you require `wtfnode` at the top of your entry point?');
        }
        return { name: '(anonymous)', file: 'unknown', line: 0 };
    }
    return thing.__callSite;
};

function dump() {
    log('info', '[WTF Node?] open handles:');

    // sort the active handles into different types for logging
    var sockets = [ ], fds = [ ], servers = [ ], _timers = [ ], processes = [ ], clusterWorkers = [ ], other = [ ];
    process._getActiveHandles().forEach(function (h) {
        // handles can be null now? early exit to guard against this
        if (!h) { return; }

        if (h instanceof Socket) {
            // stdin, stdout, etc. are now instances of socket and get listed in open handles
            // todo: a more specific/better way to identify them than the 'fd' property
            if ((h.fd != null)) { fds.push(h); }
            else { sockets.push(h); }
        }
        else if (h instanceof Server) { servers.push(h); }
        else if (h instanceof dgramSocket) { servers.push(h); }
        else if (NODE_VERSION[0] < 11 && h instanceof Timer) { _timers.push(h); }
        else if (h instanceof ChildProcess) { processes.push(h); }
        else if (h.hasOwnProperty('__worker')) { clusterWorkers.push(h); }

        // catchall
        else { other.push(h); }
    });

    if (trackedAsyncResources !== null) {
        trackedAsyncResources.forEach(function (obj) {
            if (obj.type === 'Timeout') {
                if (obj.resource[refSymbol] === true) {
                    _timers.push(obj.resource);
                }
            }
        });
    }

    if (fds.length) {
        log('info', '- File descriptors: (note: stdio always exists)');
        fds.forEach(function (s) {
            var str = '  - fd '+s.fd;
            if (s.isTTY) { str += ' (tty)'; }
            if (s._isStdio) { str += ' (stdio)'; }
            if (s.destroyed) { str += ' (destroyed)'; }
            log('info', str);

            // this event will source the origin of a readline instance, kind of indirectly
            var keypressListeners = s.listeners('keypress');
            if (keypressListeners && keypressListeners.length) {
                log('info', '    - Listeners:');
                keypressListeners.forEach(function (fn) {
                    var callSite = getCallsite(fn);
                    log('info', '      - %s: %s @ %s:%d', 'keypress', fn.name || fn.__name || callSite.name || '(anonymous)', callSite.file, callSite.line);
                });
            }
        });
    }

    // remove cluster workers from child process list
    clusterWorkers.forEach(function (p) {
        if (!p.__worker || !p.__worker.process) { return; }
        var cw = p.__worker.process,
            idx = processes.indexOf(cw);

        if (idx > -1) { processes.splice(idx, 1); }
    });

    if (processes.length) {
        log('info', '- Child processes');
        processes.forEach(function (cp) {
            var fds = [ ];
            log('info', '  - PID %s', cp.pid);
            if (!DONT_INSTRUMENT['ChildProcess']) {
                var callSite = getCallsite(cp);
                log('info', '    - Entry point: %s:%d', callSite.file, callSite.line);
            }
            if (cp.stdio && cp.stdio.length) {
                cp.stdio.forEach(function (s) {
                    if (s && s._handle && (s._handle.fd != null)) { fds.push(s._handle.fd); }
                    var idx = sockets.indexOf(s);
                    if (idx > -1) {
                        sockets.splice(idx, 1);
                    }
                });
                if (fds && fds.length) {
                    log('info', '    - STDIO file descriptors:', fds.join(', '));
                }
            }
        });
    }

    if (clusterWorkers.length) {
        log('info', '- Cluster workers');
        clusterWorkers.forEach(function (cw) {
            var fds = [ ], cp = cw.__worker.process;
            log('info', '  - PID %s', cp.pid);
            var callSite = getCallsite(cw);
            log('info', '    - Entry point: %s:%d', callSite.file, callSite.line);
        });
    }

    if (sockets.length) {
        log('info', '- Sockets:');
        sockets.forEach(function (s) {
            if (s.destroyed) {
                log('info', '  - (?:?) -> %s:? (destroyed)', s._host);
            } else if (s.localAddress) {
                log('info', '  - %s:%s -> %s:%s', s.localAddress, s.localPort, s.remoteAddress, s.remotePort);
            } else if (s._handle && (s._handle.fd != null)) {
                log('info', '  - fd %s', s._handle.fd);
            } else {
                log('info', '  - unknown socket');
            }
            var connectListeners = s.listeners('connect');
            if (connectListeners && connectListeners.length) {
                log('info', '    - Listeners:');
                connectListeners.forEach(function (fn) {
                    var callSite = getCallsite(fn);
                    log('info', '      - %s: %s @ %s:%d', 'connect', fn.name || fn.__name || callSite.name || '(anonymous)', callSite.file, callSite.line);
                });
            }
        });
    }

    if (servers.length) {
        log('info', '- Servers:');
        servers.forEach(function (s) {
            var type = 'unknown type';
            if (s instanceof HttpServer) { type = 'HTTP'; }
            else if (s instanceof HttpsServer) { type = 'HTTPS'; }
            else if (s instanceof TlsServer) { type = 'TLS'; }
            else if (s instanceof Server) { type = 'TCP'; }
            else if (s instanceof dgramSocket) { type = 'UDP'; }

            try {
                var a = s.address();
            } catch (e) {
                if (type === 'UDP') {
                    // udp sockets that haven't been bound will throw, but won't prevent exit
                    return;
                }
                throw e;
            }

            if (a) {
                if (typeof a === "string") {
                    // IPC Socket
                    log('info', '  - %s (%s)', a, type);
                } else {
                    log('info', '  - %s:%s (%s)', a.address, a.port, type);
                }
            } else {
                log('info', '  - <unknown address>'); // closed / race condition?
            }

            var eventType = (
              type === 'HTTP' || type === 'HTTPS' ? 'request' :
              type === 'TCP' || type === 'TLS' ? 'connection' :
              type === 'UDP' ? 'message' :
              'connection'
            );

            var listeners = s.listeners(eventType);

            if (listeners && listeners.length) {
                log('info', '    - Listeners:');
                listeners.forEach(function (fn) {
                    var callSite = getCallsite(fn);
                    log('info', '      - %s: %s @ %s:%d', eventType, fn.name || fn.__name || callSite.name || '(anonymous)', callSite.file, callSite.line);
                });
            }
        });
    }

    var timers = [ ], intervals = [ ];
    _timers.forEach(function (t) {
        var timer = t._list, cb, cbkey;
        if (NODE_VERSION[0] > 10) {
            timer = t;
            cbkey = timerCallback(timer);
            if (cbkey && timers.indexOf(timer) === -1) {
                cb = timer[cbkey];
                if (cb.__isInterval) {
                    intervals.push(timer);
                } else {
                    timers.push(timer);
                }
            }
        } else if (t._list) {
            // node v5ish behavior
            do {
                cbkey = timerCallback(timer);
                if (cbkey && timers.indexOf(timer) === -1) {
                    cb = timer[cbkey];
                    if (cb.__isInterval || cbkey === '_repeat') {
                        intervals.push(timer);
                    } else {
                        timers.push(timer);
                    }
                }
                timer = timer._idleNext;
            } while (!timer.constructor || timer !== t._list);
        } else {
            // node 0.12ish behavior
            _timers.forEach(function (t) {
                var timer = t;
                while ((timer = timer._idleNext)) {
                    if (timer === t) {
                        break;
                    }
                    cbkey = timerCallback(timer);
                    if (cbkey && timers.indexOf(timer) === -1) {
                        cb = timer[cbkey];
                        if (cb.__isInterval) {
                            intervals.push(timer);
                        } else {
                            timers.push(timer);
                        }
                    }
                }
            });
        }
    });

    if (timers.length) {
        log('info', '- Timers:');

        timers.forEach(function (t) {
            var fn = t[timerCallback(t)],
                callSite = getCallsite(fn);

            log('info', '  - (%d ~ %s) %s @ %s:%d', t._idleTimeout, formatTime(t._idleTimeout), fn.name || fn.__name || callSite.name || '(anonymous)', callSite.file, callSite.line);
        });
    }

    if (intervals.length) {
        log('info', '- Intervals:');

        intervals.forEach(function (t) {
            var fn = t[timerCallback(t)],
                callSite = getCallsite(fn);

            log('info', '  - (%d ~ %s) %s @ %s:%d', t._idleTimeout, formatTime(t._idleTimeout), fn.name || fn.__name || callSite.name, callSite.file, callSite.line);
        });
    }

    if (other.length) {
        log('info', '- Others:');
        other.forEach(function (o) {
            if (!o) { return; }
            if (isChannel(o)) {
                log('info', '  - %s', 'IPC channel to parent (see readme)');
            }
            else if (o.constructor) { log('info', '  - %s', o.constructor.name); }
            else { log('info', '  - %s', o); }
        });
    }
}
function isChannel(obj) {
    // node docs state process.channel added in 7.10, but _channel
    // seems to exist prior to that
    var ch = process.channel || process._channel;
    return ch && obj === ch;
}

function dumpAndExit() {
    // let other potential handlers run before exiting
    process.nextTick(function () {
        try { dump(); }
        catch (e) { log('error', e); }
        process.exit();
    });
}
function init() {
    process.on('SIGINT', dumpAndExit);
    process.on('SIGTERM', dumpAndExit);
}

module.exports = {
    dump: dump,
    init: init,
    setLogger: log.setLogger,
    resetLoggers: log.resetLoggers
};

function parseArgs() {
    if (process.argv.length < 3) {
        log('error', 'Usage: wtfnode <yourscript> <yourargs> ...');
        process.exit(1);
    }
    var moduleParams = process.argv.slice(3);
    var modulePath = path.resolve(process.cwd(), process.argv[2]);
    return [].concat(process.argv[0], modulePath, moduleParams);
}

if (module === require.main) {
    init();
    // The goal here is to invoke the given module in a form that is as
    // identical as possible to invoking `node <the_module>` directly.
    // This means massaging process.argv and using Module.runMain to convince
    // the module that it is the 'main' module.
    var newArgv = parseArgs(process.argv);
    var Module = require('module');
    process.argv = newArgv;
    Module.runMain();
}

const elListar = document.getElementById("listar");
const elConn = document.getElementById("cnt");
const elDconn = document.getElementById("dcnt");
const elMsg = document.getElementById("msg");
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/asterisk")
    .configureLogging(signalR.LogLevel.Debug)
    .build();

async function start() {
    try {
        await connection.start();
        console.log("SignalR Connected.");
    } catch (err) {
        console.log(err);
        setTimeout(start, 5000);
    }
};

connection.onclose(async () => {
    await start();
});

connection.on("ExtensionStatus", el=>{
    elMsg.innerHTML+=`${new Date().toLocaleDateString()}: ${JSON.stringify(el, null, 4)} <br>`;
});

connection.on("PeerInfo", el=>{
    elMsg.innerHTML+=`${new Date().toLocaleDateString()}: ${JSON.stringify(el, null, 4)} <br>`;
})

connection.on("NotAuthenticated", ()=>{
    console.log("Deu ruim");
})

// Start the connection.
start();

elConn.addEventListener("click",()=>{
    connection.invoke("Start")
})
elDconn.addEventListener("click", ()=>{
    elMsg.innerText= "";
    connection.invoke("Stop")
})
elListar.addEventListener("click", ()=>{
    connection.invoke("ShowExtensions")
})
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Messaging;
using System.Reflection;
using System.Threading;
using Empresa.AccesoDatos.SelfTracking;
using Empresa.ProcesarColas.MSMQ;

namespace Empresa.ProcesarColas.Operaciones
{
    public class Proceso
    {
        private string _mensajeError;
        private Empresa.ProcesarColas.MSMQ.Queue<MensajeWCFParaMSMQ> _colaEmpresa;

        /// <summary>
        /// Configura las colas de transacciones y peticiones
        /// </summary>
        public Proceso()
        {
            this._colaEmpresa = new Empresa.ProcesarColas.MSMQ.Queue<MensajeWCFParaMSMQ>(
                ConfigurationManager.AppSettings["nombreColaPeticiones"],
                ConfigurationManager.AppSettings["nombreColaTransacciones"]
                );

        }
        /// <summary>
        /// Contiene el mensaj de error en caso de ser requerido
        /// </summary>
        public string MensajeError
        {
            get { return _mensajeError; }
            set { _mensajeError = value; }
        }



        #region metodos_cliente_tcp

        /// <summary>
        /// Envia un mensaje del cliente al servidor
        /// </summary>
        /// <param name="mensaje">mensaje a enviar</param>
        public void avisaServidor(string mensaje)
        {
            try
            {
                /* cre un cliente para enviar un mensaje al servidor por TCP
                                 * para indicarle que hay mensajes en la cola de respuestas para que pueda procesar
                                 * */
                Cliente mi_clienteTCP =
                    new Cliente(ConfigurationManager.AppSettings["ipServidorEscucha"],
                        int.Parse(ConfigurationManager.AppSettings["puertoEscucha"]));
                //ahora me conecto y abro el canal de comunicacion
                mi_clienteTCP.Start();
                //envio el mensaje
                mi_clienteTCP.SendMessage(mensaje);
                //cierro el canal de comunicacion
                mi_clienteTCP.Close();
            }
            catch (Exception ex)
            {
                Exception error = new Exception("Error al establecer comunicación con el servidor para informatr que se " +
                    "despachar la cola de respuesta, Error: " + ex.Message, ex);

                //ControladorExcepciones.Procesar(error);

                //como hubo problemas mando a dormir el hilo hasta que pueda ser solucionado
                Thread.Sleep(int.Parse(ConfigurationManager.AppSettings["tiempoEsperaErrorTransaccion"]) * 6000);
                //vuelvo a procesar el evento
                avisaServidor(mensaje);
            }
        }
        #endregion metodos_cliente_tcp

        #region metodos_con_listener

        /// <summary>
        /// Permite convertir los parametros que se encuentran en la cola msmq transformados en xml a los tipos correspondientes de selftracking
        /// </summary>
        /// <param name="mensaje">mensaje a convertir</param>
        /// <returns>Instancia de mensaje MSMQ con los tipos originales</returns>
        private MensajeWCFParaMSMQ SetearParametrosWCF(MensajeWCFParaMSMQ mensaje)
        {
            //creamos una instancia del contexto del selftracking
            using (var contexto = new Empresa.AccesoDatos.SelfTracking.ContextoEmpresaSelfTracking())
            {
                //creamos una instacia de una lista de objetos
                List<object> listaParametros = new List<object>();
                short i = 0;
                foreach (var item in mensaje.ParametrosWCF)
                {
                    //el parametro es de tipo Empresa.accesodatos lo convertimos de xml a su intancia respectiva
                    if (mensaje.ListaTipoParametros[i] !=null && !string.IsNullOrEmpty(mensaje.ListaTipoParametros[i].Tipo))
                    {
                        var tipo = Assembly.GetAssembly(contexto.GetType()).CreateInstance(mensaje.ListaTipoParametros[i].Tipo);
                        //convertimos de xml a objeto selftracking
                        listaParametros.Add(DataHelper.DeserializeEntity(tipo.GetType(), item.ToString()));

                    }//es un listado de objetos Empresa.accesodatos
                    else if (mensaje.ListaTipoParametros[i] != null && mensaje.ListaTipoParametros[i].TipoLista != null)
                    {
                        //creamos una instancia de una lista generica de un tipo especifico de Empresa.accesodatos usando reflexion
                        var tipoGenerico = Assembly.GetAssembly(contexto.GetType()).CreateInstance(mensaje.ListaTipoParametros[i].TipoLista[0]);
                        bool esguid = false;
                        if (tipoGenerico == null)
                        {
                            tipoGenerico = Guid.NewGuid();
                            esguid = true;
                        }
                        Type listType = typeof(List<>).MakeGenericType(new Type[] { tipoGenerico.GetType() });
                        var parametrosLista = Activator.CreateInstance(listType);

                        //obtenemos la lista de los tipos de objetos serializados con xml
                        List<string> listaObjetos = (List<string>)item;
                        short j = 0;
                        //creamos instancia de objetos de tipo Empresa.accesodatos que eran instancias de xml
                        foreach (var itemLista in mensaje.ListaTipoParametros[i].TipoLista)
                        {                            
                            var tipo = Assembly.GetAssembly(contexto.GetType()).CreateInstance(itemLista);
                            if (tipo == null)
                                tipo = Guid.NewGuid();
                            List<object> objetos  = new List<object>();
                            if(esguid)
                                objetos.Add(Guid.Parse(listaObjetos[j].ToString()));
                            else
                                objetos.Add(DataHelper.DeserializeEntity(tipo.GetType(), listaObjetos[j].ToString()));
                            //añadimos un objeto del tipo Empresa.accesodatos a la lista genrca mediante reflexion
                            parametrosLista.GetType().GetMethod("Add").Invoke(parametrosLista, objetos.ToArray());
                                                        j++;
                        }
                        //setemoa la nueva lista con los tipo de datos originales
                        listaParametros.Add(parametrosLista);
                    }
                    else // si era un objeto del tipo comun lo mantenemos igual
                    {
                        listaParametros.Add(item);
                    }
                    i++;
                }
                //setemamos la lista final con los paramteris convertidos y no convertidos
                mensaje.ParametrosWCF = listaParametros.ToArray();
            }
            return mensaje;
        }

        
        
        /// <summary>
        /// Permite despachar los mensajes que se encuentran en la cola MSMQ
        /// </summary>
        /// <param name="esTransaccion">Indica si se vana procesar los mensajes de la cola de trabsacciones</param>
        private void ProcesarCola(bool esTransaccion)
        {
            //obtenemos la cola a procesar
            MessageQueue cola = (esTransaccion) ? _colaEmpresa.ColaTransacciones : _colaEmpresa.ColaPeticiones;
            //bloqueamos la cola para uso exclusivo
            lock (cola)
            {
                //obtenemos el tiempo de espera en caso de error para poder reintentar
                int timepoEspera = (esTransaccion) ? int.Parse(ConfigurationManager.AppSettings["tiempoEsperaErrorTransaccion"]) : int.Parse(ConfigurationManager.AppSettings["tiempoEsperaErrorPeticion"]);

                //extraigo un mensaje de la cola de transacciones
                MensajeWCFParaMSMQ mensajeCola = _colaEmpresa.ExtraeCuerpoMensajeCola(cola);

                //proceso la cola mientras no haya mas mensajes en la cola
                bool esReintento = false;
                if (mensajeCola == null)
                {
                    Console.WriteLine(DateTime.Now.ToString() + " No hay mensajes por despachar en la cola");
                    EventLog.WriteEntry("ServicioColasEmpresa", DateTime.Now.ToString() + " No hay mensajes por despachar en la cola");
                }
                while (mensajeCola != null)
                {
                    //crea mos una instacia del servicio de colas
                    InstanciaServicio instanciaServicio = new InstanciaServicio();
                    if (!esReintento)
                    {
                        if (mensajeCola.EsRIAService)
                        {

                            if (mensajeCola.OperacionRIA != EnumOperacionRIA.Delete)//invocamos al metodo de borrado fisico de la base
                            {
                                ProcesoServiciosRIA procesoRIA = new ProcesoServiciosRIA();

                                IObjectWithChangeTracker entidad = procesoRIA.ConsultaEntidadPorId(mensajeCola.NombreEntidadRIA, mensajeCola.IdEntidadRIA);
                                entidad = procesoRIA.CambiarEstadoEntidad(entidad, mensajeCola.OperacionRIA);
                                mensajeCola.AsigarMetodoWCF("GuardarCatalogo", entidad);
                            }
                            else //invocamos al metodo para inserrtar o actualizar
                            {
                                Empresa.ProcesarColas.ProxyServicioCatalogos.MensajeWCFParaMSMQ mensajeProxy = new ProxyServicioCatalogos.MensajeWCFParaMSMQ();
                                mensajeProxy.EsRIAService = true;
                                mensajeProxy.NombreEntidadRIA = mensajeCola.NombreEntidadRIA;
                                //hacemos una replica del objeto clave tabla que se construye con la referencia al objeto propio de esta clase
                                mensajeProxy.IdEntidadRIA = new List<ProxyServicioCatalogos.ClaveTabla>();
                                foreach (var item in mensajeCola.IdEntidadRIA)
                                {
                                    var clave = new ProxyServicioCatalogos.ClaveTabla();
                                    clave.Id = item.Id;
                                    clave.NombreColumna = item.NombreColumna;
                                    mensajeProxy.IdEntidadRIA.Add(clave);
                                }
                                mensajeCola.AsigarMetodoWCF("EliminarCatalogo", mensajeProxy);
                            }
                            mensajeCola.NombreServicioWCF = EnumServiciosWCF.ServicioRIA;

                        }
                        else // es un mensaj del tipo WCF
                            mensajeCola = SetearParametrosWCF(mensajeCola);
                    }
                    //procesamos el mensaje y obtenemos la respuesta del servidor remoto
                    Resultado respuesta = (Resultado)instanciaServicio.InvocarMetodo(mensajeCola.URLDestino, mensajeCola.NombreServicioWCF, mensajeCola.MetodoWCF, mensajeCola.ParametrosWCF);
                    //se proceso correctamente el mensaje
                    if (respuesta.Estado)
                    {
                        //removemos el mensaje correctamente procesado
                        _colaEmpresa.RemoverMensajeCola(_colaEmpresa.ColaTransacciones, mensajeCola.IdMensajeMSMQ);
                        mensajeCola = _colaEmpresa.ExtraeCuerpoMensajeCola(cola);
                        esReintento = false;
                        //notificamos el proceso al event viewer
                        Console.WriteLine(DateTime.Now.ToString() + " Se ha despachado correctamente un mensaje de la cola");
                        EventLog.WriteEntry("ServicioColasEmpresa", DateTime.Now.ToString() + " Se ha despachado correctamente un mensaje de la cola");
                    }
                    else //hubo errores al procesar el mensaje en el servidor remoto
                    {
                        Console.WriteLine(respuesta.Mensaje);
                        EventLog.WriteEntry("ServicioColasEmpresa", DateTime.Now.ToString() + " Error: " + respuesta.Mensaje);
                        esReintento = true;
                        //dormimos el proceso por un tiempo determinado antes de reintentar
                        Thread.Sleep(timepoEspera * 60000);
                    }

                }
            }
        }
        /// <summary>
        /// procesa la cola de transacciones
        /// </summary>
        public void procesaTransaccion()
        {
            ProcesarCola(true);
        }

        /// <summary>
        /// procesa la cola de peticiones
        /// </summary>
        public void procesaPeticiones()
        {
            ProcesarCola(false);
        }
        #endregion metodos_con_listener

        #region metodos_sin_listener
        /// <summary>
        ///´procesa la cola de transacciones mediante un bule infinito
        /// </summary>
        public void procesaTransaccionBucle()
        {
            while (true)
            {
                ProcesarCola(true);
            }
        }
        /// <summary>
        /// procesa la cola de peticiones mediante un bucle infinito
        /// </summary>
        public void procesaPeticionaBucle()
        {
            while (true)
            {
                ProcesarCola(true);
            }
        }
        #endregion metodos_sin_listener
    }

    


}

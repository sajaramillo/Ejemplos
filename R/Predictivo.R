# load the data from a CSV
SRC_PATH <-"c:/curso/"
data <- read.csv("c:/curso/train.csv", stringsAsFactors = TRUE)

cat("\014") 

head(data,10)
colnames(data)

str(data)

# split the data 80% train/20% test
sample_idx <- sample(nrow(data), nrow(data)*0.8)
data_train <- data[sample_idx, ]
data_test <- data[-sample_idx, ]

# create a linear model using the training partition
gm_pct_model <- lm(Credit_History ~ Gender + Married + LoanAmount + Property_Area + Dependents, data_train,qr ="class")

library(rpart)
tree_model = rpart(Credit_History ~ ., data = data_train)


# score the test data and plot pred vs. obs 
plot(data.frame('Predicted'=predict(gm_pct_model, data_test), 'Observed'=data_test$Credit_History))

# score the test data and append it as a new column (for later use)
new_data <- cbind(data_test,'PREDICTED_CREDIT_HISTORY'=predict(gm_pct_model, data_test))



# score an individual row
predicted_gm_rate <- predict(gm_pct_model, data_test[1,])

# score the test data and plot pred vs. obs 
plot(data.frame('Predicted'=predict(tree_model, data_test), 'Observed'=data_test$Credit_History))

# score the test data and append it as a new column (for later use)
new_data <- cbind(data_test,'PREDICTED_CREDIT_HISTORY'=predict(tree_model, data_test))

head(new_data,2)

# score an individual row
predicted_gm_rate <- predict(tree_model, data_test[1,])

head(new_data$PREDICTED_CREDIT_HISTORY,5)

predict(tree_model, data_test)

plotcp(tree_model,data)
plotcp(gm_pct_model)

summary(tree_model)


# create attractive postscript plot of tree 
post(tree_model, file = "c:/curso/tree.ps", 
     title = "Classification Tree for Kyphosis")





# prune the tree 
pfit<- prune(tree_model, cp=   tree_model$cptable[which.min(tree_model$cptable[,"xerror"]),"CP"])

# plot the pruned tree 
plot(tree_model, uniform=TRUE, 
     main="Pruned Classification Tree for Kyphosis")
text(tree_model, use.n=TRUE, all=TRUE, cex=.8)
post(tree_model, file = "c:/curso/ptree.ps", 
     title = "Pruned Classification Tree for Kyphosis")

prediccion=predict(tree_model, newdata = data_train)
prediccion

head(data)

as.integer(as.logical(data$Credit_History))

data$Credit_History=as.logical(data$Credit_History)

table(data_train$Credit_History)


table(prediccion)

nrow(prediccion)
str(prediccion)

sum(is.na(data_test$Credit_History))

data_test$Credit_History[is.nan(data_test$Credit_History)]=0

#replace null with 0
data_test[["Credit_History"]][is.na(data_test[["Credit_History"]])] <- 0


